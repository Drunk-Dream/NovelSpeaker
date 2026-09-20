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
    public void Wpf_tests_keep_desktop_creation_and_binding_inside_the_shared_testkit()
    {
        var wpfRoot = Path.Combine(Repository.RootPath, "tests", "NovelSpeaker.App.WpfTests");
        var sources = Directory.EnumerateFiles(wpfRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(sources, source => source.Contains("SetThreadDesktop", StringComparison.Ordinal));
        Assert.DoesNotContain(sources, source => source.Contains("CreateDesktop", StringComparison.Ordinal));
        Assert.DoesNotContain(sources, source => source.Contains("OpenInputDesktop", StringComparison.Ordinal));
        Assert.DoesNotContain(
            sources,
            source => source.Contains(string.Concat("NOVELSPEAKER_TEST_", "SHOW_WINDOWS"), StringComparison.Ordinal));
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

}
