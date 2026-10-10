using System.Text.RegularExpressions;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class BehaviorDebtBaselineTests
{
    private static readonly ArchitectureTestRepository Repository = ArchitectureTestRepository.Locate();

    [Fact]
    public void App_consumes_cache_views_without_invalidation_or_repair_protocols()
    {
        var forbidden = new Regex(@"\b(?:ICacheInvalidationSink|ICacheInvalidationCoordinator|CacheInvalidationCoordinator|CacheInvalidationAspect|CacheInvalidationScope|CacheInvalidationBatch|CacheInvalidation|ICacheCatalog|ICacheCoverageQuery|ICachePlanRepairRequestor|SpeechPlanRepairRequestor|ISpeechPlanRepairCoordinator|SpeechPlanRepairCoordinator|SpeechPlanRepairRequest)\b");
        foreach (var file in Repository.ReadProductSourceFiles()
                     .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.App"))
        {
            Assert.False(forbidden.IsMatch(file.Content), file.RelativePath);
            if (!file.RelativePath.StartsWith("src/NovelSpeaker.App/Bootstrap/", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("ICacheChangeLifetime", file.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("ISpeechPlanRepairLifetime", file.Content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Non_platform_app_code_does_not_perform_user_document_io_directly()
    {
        var appRoot = Absolute("src/NovelSpeaker.App");
        var excludedFragments = new[]
        {
            Path.DirectorySeparatorChar + "Bootstrap" + Path.DirectorySeparatorChar,
            Path.DirectorySeparatorChar + "Shared" + Path.DirectorySeparatorChar +
            "Presentation" + Path.DirectorySeparatorChar + "Platform" + Path.DirectorySeparatorChar
        };
        var sources = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !excludedFragments.Any(path.Contains))
            .Select(File.ReadAllText);

        foreach (var source in sources)
        {
            Assert.DoesNotContain("File.ReadAll", source, StringComparison.Ordinal);
            Assert.DoesNotContain("File.WriteAll", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Directory.Delete", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.Win32", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Async_production_code_does_not_use_synchronous_waits()
    {
        var sources = Directory.EnumerateFiles(
                Absolute("src/NovelSpeaker.Infrastructure"),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(sources, source => source.Contains("GetAwaiter().GetResult()", StringComparison.Ordinal));
        Assert.DoesNotContain(
            sources,
            source => Regex.IsMatch(source, @"\.Result\s*[\]\),;]"));
        Assert.DoesNotContain(sources, source => source.Contains(".Wait(", StringComparison.Ordinal));
    }

    private static string Absolute(string relativePath) =>
        Path.Combine(Repository.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
