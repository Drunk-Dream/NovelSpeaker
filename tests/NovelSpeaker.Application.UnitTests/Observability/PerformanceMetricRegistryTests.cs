using NovelSpeaker.Application.Observability;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Observability;

public sealed class PerformanceMetricRegistryTests
{
    [Fact]
    public void Default_registry_contains_only_low_cardinality_first_batch_metrics()
    {
        var registry = PerformanceMetricRegistry.Default;

        Assert.Contains(registry.Definitions, metric => metric.Name == "operation.count");
        Assert.Contains(registry.Definitions, metric => metric.Name == "operation.duration");
        Assert.Contains(registry.Definitions, metric => metric.Name == "process.cpu.percent");
        Assert.All(registry.Definitions, metric => Assert.All(metric.AllowedTags.Values, values => Assert.NotEmpty(values)));
    }

    [Fact]
    public void Forbidden_or_high_cardinality_tags_are_rejected()
    {
        foreach (var tagName in new[] { "bookId", "chapter", "path", "url", "content", "requestQuery" })
        {
            Assert.Throws<ArgumentException>(() => new PerformanceMetricDefinition(
                "test.metric",
                PerformanceMetricType.Counter,
                "count",
                "test",
                allowedTags: new Dictionary<string, IReadOnlyList<string>>
                {
                    [tagName] = ["stable"]
                }));
        }
    }

    [Fact]
    public void Histogram_buckets_must_be_strictly_increasing()
    {
        Assert.Throws<ArgumentException>(() => new PerformanceMetricDefinition(
            "test.histogram",
            PerformanceMetricType.Histogram,
            "ms",
            "test",
            [10, 10]));
    }
}
