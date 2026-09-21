namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Privacy-reviewed metric metadata. Values are deliberately finite and low-cardinality.
/// </summary>
public sealed class PerformanceMetricDefinition
{
    private static readonly IReadOnlySet<string> AllowedTagNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "operation",
        "outcome",
        "kind",
        "status",
        "reason",
        "priority",
        "source",
        "result",
        "state",
        "component",
        "platform"
    };

    public PerformanceMetricDefinition(
        string name,
        PerformanceMetricType type,
        string unit,
        string description,
        IEnumerable<double>? histogramBuckets = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? allowedTags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')) ||
            !char.IsAsciiLetter(name[0]) ||
            !char.IsAsciiLetterOrDigit(name[^1]))
        {
            throw new ArgumentException("Metric names must use stable lower-case tokens.", nameof(name));
        }

        if (name.Any(character => character is >= 'A' and <= 'Z'))
        {
            throw new ArgumentException("Metric names must use lower-case tokens.", nameof(name));
        }

        var buckets = (histogramBuckets ?? []).ToArray();
        if (type == PerformanceMetricType.Histogram &&
            (buckets.Length == 0 || buckets.Any(value => !double.IsFinite(value)) ||
             buckets.Zip(buckets.Skip(1)).Any(pair => pair.First >= pair.Second)))
        {
            throw new ArgumentException("Histograms require strictly increasing finite buckets.", nameof(histogramBuckets));
        }

        if (type != PerformanceMetricType.Histogram && buckets.Length != 0)
        {
            throw new ArgumentException("Only histograms may declare buckets.", nameof(histogramBuckets));
        }

        var copiedTags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (allowedTags is not null && allowedTags.Count > 4)
        {
            throw new ArgumentException("Metrics may declare at most four low-cardinality tags.", nameof(allowedTags));
        }

        foreach (var pair in allowedTags ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            ValidateTagName(pair.Key);
            var values = pair.Value?.ToArray() ?? throw new ArgumentException("Tag values cannot be null.", nameof(allowedTags));
            if (values.Length == 0 || values.Length > 32 || values.Any(value => !IsStableTagValue(value)))
            {
                throw new ArgumentException("Tags must declare finite lower-case values.", nameof(allowedTags));
            }

            copiedTags.Add(pair.Key, Array.AsReadOnly(values));
        }

        Name = name;
        Type = type;
        Unit = unit;
        Description = description;
        HistogramBuckets = Array.AsReadOnly(buckets);
        AllowedTags = new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlyList<string>>(copiedTags);
    }

    public string Name { get; }
    public PerformanceMetricType Type { get; }
    public string Unit { get; }
    public string Description { get; }
    public IReadOnlyList<double> HistogramBuckets { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AllowedTags { get; }

    public void ValidateTags(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            if (AllowedTags.Count != 0)
            {
                throw new ArgumentException($"Metric '{Name}' requires its declared tags.", nameof(tags));
            }

            return;
        }

        if (tags.Count != AllowedTags.Count || tags.Keys.Any(key => !AllowedTags.ContainsKey(key)))
        {
            throw new ArgumentException($"Metric '{Name}' received undeclared or incomplete tags.", nameof(tags));
        }

        foreach (var pair in tags)
        {
            if (!AllowedTags[pair.Key].Contains(pair.Value, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Metric '{Name}' received an invalid value for tag '{pair.Key}'.", nameof(tags));
            }
        }
    }

    private static void ValidateTagName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        if (!AllowedTagNames.Contains(name) ||
            name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')) ||
            !char.IsAsciiLetter(name[0]) ||
            normalized.Contains("book", StringComparison.Ordinal) ||
            normalized.Contains("chapter", StringComparison.Ordinal) ||
            normalized.Contains("path", StringComparison.Ordinal) ||
            normalized.Contains("url", StringComparison.Ordinal) ||
            normalized.Contains("query", StringComparison.Ordinal) ||
            normalized.Contains("content", StringComparison.Ordinal) ||
            normalized.Contains("text", StringComparison.Ordinal) ||
            normalized.Contains("regex", StringComparison.Ordinal) ||
            normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("header", StringComparison.Ordinal) ||
            normalized.Contains("body", StringComparison.Ordinal) ||
            normalized.EndsWith("id", StringComparison.Ordinal) ||
            normalized.EndsWith("name", StringComparison.Ordinal) ||
            normalized.Contains("session", StringComparison.Ordinal) ||
            normalized.Contains("correlation", StringComparison.Ordinal) ||
            normalized.Contains("fingerprint", StringComparison.Ordinal) ||
            normalized.Contains("timestamp", StringComparison.Ordinal))
        {
            throw new ArgumentException("Metric tags must be low-cardinality privacy-safe tokens.", nameof(name));
        }
    }

    private static bool IsStableTagValue(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 32 &&
        value.All(character => (character is >= 'a' and <= 'z') ||
                               char.IsAsciiDigit(character) ||
                               character is '.' or '-' or '_');
}
