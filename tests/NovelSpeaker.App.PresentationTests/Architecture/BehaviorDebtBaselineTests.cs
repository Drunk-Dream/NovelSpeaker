using System.Xml.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class BehaviorDebtBaselineTests
{
    private static readonly ArchitectureTestRepository Repository = ArchitectureTestRepository.Locate();

    [Fact]
    public void Restore_graph_always_includes_the_release_runtime_identifier()
    {
        var buildProperties = XDocument.Load(Absolute("Directory.Build.props"));
        var properties = buildProperties.Descendants("PropertyGroup")
            .Elements()
            .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.Ordinal);

        Assert.Equal("true", properties["RestorePackagesWithLockFile"]);
        Assert.Equal("win-x64", properties["RuntimeIdentifiers"]);
    }

    [Fact]
    public void Async_void_page_events_delegate_through_the_shared_exception_boundary()
    {
        var featuresRoot = Absolute("src/NovelSpeaker.App/Features");
        var pages = Directory.EnumerateFiles(featuresRoot, "*Page.xaml.cs", SearchOption.AllDirectories);

        foreach (var path in pages)
        {
            var source = File.ReadAllText(path);
            if (!source.Contains("async void", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Contains("PageEventOperationRunner", source, StringComparison.Ordinal);
            Assert.Contains("_eventOperations.RunAsync(", source, StringComparison.Ordinal);
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

    [Fact]
    public void Test_audio_fixtures_are_owned_by_tests_and_excluded_from_the_app()
    {
        var expected = new[] { "corrupt-tone.mp3", "demo-tone.mp3", "demo-tone.wav" };
        var audioDirectory = Absolute("tests/NovelSpeaker.Infrastructure.IntegrationTests/TestAssets/Audio");
        var actual = Directory.EnumerateFiles(audioDirectory)
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expected.Order(StringComparer.OrdinalIgnoreCase), actual);
        Assert.False(Directory.Exists(Absolute("src/NovelSpeaker.App/Assets/Audio")));

        var projectText = File.ReadAllText(
            Absolute("tests/NovelSpeaker.Infrastructure.IntegrationTests/NovelSpeaker.Infrastructure.IntegrationTests.csproj"));
        Assert.Contains(@"TestAssets\Audio\*.*", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain(@"src\NovelSpeaker.App\Assets\Audio", projectText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_and_quality_workflows_keep_locked_build_test_and_package_boundaries()
    {
        var release = File.ReadAllText(Absolute(".github/workflows/release.yml"));
        var quality = File.ReadAllText(Absolute(".github/workflows/quality-matrix.yml"));

        Assert.Contains("uses: ./.github/workflows/quality-matrix.yml", release, StringComparison.Ordinal);
        Assert.Contains(
            "dotnet publish src/NovelSpeaker.App/NovelSpeaker.App.csproj -c Release -r win-x64 --self-contained true --no-restore",
            release,
            StringComparison.Ordinal);
        Assert.Contains("TestAssets", release, StringComparison.Ordinal);
        Assert.Contains("StyleGallery", release, StringComparison.Ordinal);
        Assert.Contains("visual-review", release, StringComparison.Ordinal);

        foreach (var command in new[]
                 {
                     "dotnet restore --locked-mode -r win-x64",
                     "dotnet format --verify-no-changes --no-restore",
                     "dotnet build -c Release --no-restore",
                     "dotnet test",
                     "matrix.project"
                 })
        {
            Assert.Contains(command, quality, StringComparison.Ordinal);
        }

        Assert.Contains("NovelSpeaker.Domain.UnitTests", quality, StringComparison.Ordinal);
        Assert.DoesNotContain("retry", quality, StringComparison.OrdinalIgnoreCase);
    }

    private static string Absolute(string relativePath) =>
        Path.Combine(Repository.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
