namespace NovelSpeaker.Application.Observability;

/// <summary>
/// The first stable performance vocabulary. It intentionally describes operations rather than user content.
/// </summary>
public sealed class PerformanceMetricRegistry
{
    private readonly IReadOnlyDictionary<string, PerformanceMetricDefinition> _definitions;

    public PerformanceMetricRegistry(IEnumerable<PerformanceMetricDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var values = definitions.ToArray();
        if (values.Any(definition => definition is null) ||
            values.GroupBy(definition => definition.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Metric definitions must be non-null and uniquely named.", nameof(definitions));
        }

        _definitions = values.ToDictionary(definition => definition.Name, StringComparer.Ordinal);
    }

    public static PerformanceMetricRegistry Default { get; } = CreateDefault();

    public IReadOnlyList<PerformanceMetricDefinition> Definitions =>
        _definitions.Values.OrderBy(definition => definition.Name, StringComparer.Ordinal).ToArray();

    public PerformanceMetricDefinition Get(string name) =>
        _definitions.TryGetValue(name, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Metric '{name}' is not registered.");

    private static PerformanceMetricRegistry CreateDefault()
    {
        var operationTags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["operation"] = OperationCatalog.All.Select(operation => operation.Id.Value).ToArray(),
            ["outcome"] = ["succeeded", "failed", "cancelled"]
        };
        double[] durationBuckets = [1d, 5d, 10d, 25d, 50d, 100d, 250d, 500d, 1000d, 2500d, 5000d, 10000d];

        return new PerformanceMetricRegistry(
        [
            new PerformanceMetricDefinition(
                "operation.count",
                PerformanceMetricType.Counter,
                "count",
                "Completed stable operations.",
                allowedTags: operationTags),
            new PerformanceMetricDefinition(
                "operation.duration",
                PerformanceMetricType.Histogram,
                "ms",
                "Duration of completed stable operations.",
                durationBuckets,
                operationTags),
            new PerformanceMetricDefinition(
                "process.cpu.percent",
                PerformanceMetricType.Gauge,
                "percent",
                "Process CPU utilization snapshot."),
            new PerformanceMetricDefinition(
                "process.working-set.bytes",
                PerformanceMetricType.Gauge,
                "bytes",
                "Process working-set snapshot."),
            new PerformanceMetricDefinition(
                "process.managed-heap.bytes",
                PerformanceMetricType.Gauge,
                "bytes",
                "Managed heap size snapshot.")
        ]);
    }
}
