using System.Text.RegularExpressions;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class TestCleanupArchitectureTests
{
    private static readonly ArchitectureTestRepository Repository = ArchitectureTestRepository.Locate();

    [Fact]
    public void Wpf_tests_use_the_shared_window_host_boundary()
    {
        var wpfRoot = Path.Combine(Repository.RootPath, "tests", "NovelSpeaker.App.WpfTests");
        var sources = Directory.EnumerateFiles(wpfRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(sources, source => Regex.IsMatch(source, @"\.Show\s*\(\s*\)"));
        Assert.DoesNotContain(sources, source => Regex.IsMatch(source, @"ShowDialog\s*\("));
        Assert.DoesNotContain(sources, source => source.Contains("SetApartmentState(", StringComparison.Ordinal));
    }

    [Fact]
    public void Test_sources_do_not_use_sleep_or_finite_delay_as_a_synchronization_point()
    {
        var testsRoot = Path.Combine(Repository.RootPath, "tests");
        var sources = Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(sources, source => Regex.IsMatch(source, @"Thread\.Sleep\s*\("));
        Assert.DoesNotContain(
            sources,
            source => Regex.IsMatch(
                source,
                @"Task\.Delay\s*\(\s*(?:\d+|TimeSpan\.From(?:Milliseconds|Seconds|Minutes)\s*\()"));
    }

    [Fact]
    public void Visual_artifact_generators_are_explicitly_gated()
    {
        var files = new[]
        {
            "tests/NovelSpeaker.App.WpfTests/Ui/StyleGallerySceneTests.cs",
            "tests/NovelSpeaker.App.WpfTests/Ui/MediaControlStyleTests.cs",
            "tests/NovelSpeaker.App.WpfTests/Desktop/MiniPlayerWindowTests.cs"
        };

        foreach (var relativePath in files)
        {
            var source = File.ReadAllText(Path.Combine(Repository.RootPath, relativePath));
            Assert.Contains("VisualArtifactTestGuard.IsEnabled", source, StringComparison.Ordinal);
        }

        var architectureSource = File.ReadAllText(Path.Combine(
            Repository.RootPath,
            "tests",
            "NovelSpeaker.App.WpfTests",
            "Architecture",
            "VisualStyleArchitectureTests.cs"));
        Assert.DoesNotContain("style-ownership-audit.json", architectureSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Pure_view_model_tests_do_not_return_to_the_wpf_project()
    {
        var wpfRoot = Path.Combine(Repository.RootPath, "tests", "NovelSpeaker.App.WpfTests");
        var pureNames = Directory.EnumerateFiles(wpfRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetFileName(path))
            .Where(name => name.Contains("ViewModelTests", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(pureNames);
    }

    [Fact]
    public void Wpf_testkit_has_one_namespace_and_explicit_failure_diagnostics_boundary()
    {
        var testKitRoot = Path.Combine(Repository.RootPath, "tests", "TestKit", "Wpf");
        var expectedFiles = new[]
        {
            "WpfTestHost.cs",
            "WpfControlHost.cs",
            "WpfWindowHost.cs",
            "WpfFailureDiagnostics.cs",
            "VisualTreeTestHelper.cs",
            "PageVisualReviewHarness.cs",
            "WindowVisualReviewHarness.cs",
            "TransientPopupVisualRenderer.cs"
        };

        Assert.Equal(
            expectedFiles.Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(testKitRoot, "*.cs")
                .Select(Path.GetFileName)
                .Where(static name => name is not "VisualArtifactTestGuard.cs")
                .Order(StringComparer.Ordinal));
        Assert.All(expectedFiles, file =>
            Assert.Contains(
                "namespace NovelSpeaker.TestKit.Wpf;",
                File.ReadAllText(Path.Combine(testKitRoot, file)),
                StringComparison.Ordinal));
    }
}
