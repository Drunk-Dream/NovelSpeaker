using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Versioned identity of settings and rules that can change a chapter speech plan.
/// </summary>
public sealed record TextProfileFingerprint(
    int SchemaVersion,
    Fingerprint Value)
{
    public const int CurrentSchemaVersion = 1;

    public ReadOnlyMemory<byte> Bytes => Value.Bytes;

    public string Hex => Value.Hex;

    public static TextProfileFingerprint Create(
        TextSegmentationOptions options,
        IEnumerable<RegexReplacementRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var normalized = options.Normalize();
        var writer = new CanonicalIdentityWriter();
        writer.Add("schema", CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.Add("segmenter", "text-segmenter-v1");
        writer.Add("split", normalized.EnableLongParagraphSplitting ? "1" : "0");
        writer.Add("threshold", normalized.LongParagraphThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var speechRules = rules
            .Where(rule => rule.IsEnabled &&
                (rule.Scope is RegexReplacementScope.Speech or RegexReplacementScope.Both))
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.Id)
            .ToArray();
        writer.Add("rule-count", speechRules.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var rule in speechRules)
        {
            writer.Add("rule-id", rule.Id.ToString("N"));
            writer.Add("rule-order", rule.SortOrder.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.Add("rule-scope", rule.Scope.ToString());
            writer.Add("rule-pattern", rule.Pattern);
            writer.Add("rule-replacement", rule.Replacement);
        }

        return new TextProfileFingerprint(CurrentSchemaVersion, writer.Build());
    }
}
