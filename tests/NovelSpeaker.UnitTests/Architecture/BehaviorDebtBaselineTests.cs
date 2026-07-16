using System.Xml.Linq;
using Xunit;

namespace NovelSpeaker.UnitTests.Architecture;

public sealed class BehaviorDebtBaselineTests
{
    private static readonly ArchitectureTestRepository Repository = ArchitectureTestRepository.Locate();

    [Fact]
    public void Rule_pages_register_and_unregister_the_global_navigation_guard()
    {
        var pagePaths = new[]
        {
            "src/NovelSpeaker.App/Pages/ChapterRulesPage.xaml.cs",
            "src/NovelSpeaker.App/Pages/RegexReplacementRulesPage.xaml.cs",
            "src/NovelSpeaker.App/Pages/TtsRulesPage.xaml.cs"
        };
        foreach (var relativePath in pagePaths)
        {
            var page = File.ReadAllText(Absolute(relativePath));
            Assert.Contains("INavigationGuardService", page, StringComparison.Ordinal);
            Assert.Contains("Register(ViewModel.ConfirmLeaveAsync)", page, StringComparison.Ordinal);
            Assert.Contains("_guardRegistration?.Dispose()", page, StringComparison.Ordinal);
            Assert.Contains("Loaded += OnLoaded", page, StringComparison.Ordinal);
            Assert.Contains("Unloaded += OnUnloaded", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Test_audio_fixtures_are_owned_by_tests_and_excluded_from_the_app()
    {
        var fixtureNames = new[]
        {
            "corrupt-tone.mp3",
            "demo-tone.mp3",
            "demo-tone.wav"
        };
        var audioDirectory = Absolute("tests/NovelSpeaker.UnitTests/TestAssets/Audio");
        var actual = Directory.EnumerateFiles(audioDirectory)
            .Select(path => Path.GetFileName(path)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AssertEqualSet(fixtureNames, actual);

        var appAudioDirectory = Absolute("src/NovelSpeaker.App/Assets/Audio");
        Assert.False(Directory.Exists(appAudioDirectory));

        var appProjectText = File.ReadAllText(Absolute("src/NovelSpeaker.App/NovelSpeaker.App.csproj"));
        Assert.DoesNotContain(@"Assets\Audio", appProjectText, StringComparison.OrdinalIgnoreCase);

        var testProject = XDocument.Load(Absolute("tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj"));
        var testProjectText = File.ReadAllText(Absolute("tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj"));
        Assert.DoesNotContain(@"src\NovelSpeaker.App\Assets\Audio", testProjectText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(testProject.Descendants("Content"), element =>
            string.Equals(element.Attribute("Include")?.Value, @"TestAssets\Audio\*.*", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_workflow_keeps_the_locked_quality_gate_and_package_validation()
    {
        var workflow = File.ReadAllText(Absolute(".github/workflows/release.yml"));
        var requiredFragments = new[]
        {
            "dotnet restore --locked-mode -r win-x64",
            "dotnet format --verify-no-changes --no-restore",
            "dotnet build -c Release --no-restore",
            "dotnet test -c Release --no-build",
            "dotnet publish src/NovelSpeaker.App/NovelSpeaker.App.csproj -c Release -r win-x64 --self-contained true --no-restore",
            "NovelSpeaker.App.exe",
            "THIRD-PARTY-NOTICES.txt",
            "demo-tone.wav",
            "demo-tone.mp3",
            "corrupt-tone.mp3",
            "Package contains test audio fixture"
        };

        foreach (var fragment in requiredFragments)
        {
            Assert.Contains(fragment, workflow, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Shell_navigation_entry_points_use_the_guarded_navigation_boundary()
    {
        var mainWindow = File.ReadAllText(Absolute("src/NovelSpeaker.App/MainWindow.xaml.cs"));
        Assert.Contains("OnRootNavigationViewNavigating", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_guardedNavigationService.NavigateAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_guardedNavigationService.NavigateWithHierarchyAsync", mainWindow, StringComparison.Ordinal);

        var shortcuts = File.ReadAllText(Absolute("src/NovelSpeaker.App/Input/KeyboardShortcutCoordinator.cs"));
        Assert.Contains("IGuardedNavigationService navigation", shortcuts, StringComparison.Ordinal);
        Assert.Contains("_navigation.GoBackAsync", shortcuts, StringComparison.Ordinal);
        Assert.Contains("typeof(SettingsPage)", shortcuts, StringComparison.Ordinal);

        var shellViewModel = File.ReadAllText(Absolute("src/NovelSpeaker.App/ViewModels/MainWindowViewModel.cs"));
        Assert.Contains("IGuardedNavigationService guardedNavigationService", shellViewModel, StringComparison.Ordinal);
        Assert.Contains("_guardedNavigationService.NavigateWithHierarchyAsync", shellViewModel, StringComparison.Ordinal);
        Assert.Contains("PlayerNavigationMode.ReturnToCurrentSession", shellViewModel, StringComparison.Ordinal);
    }

    private static string Absolute(string relativePath) =>
        Path.Combine(Repository.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void AssertEqualSet(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        Assert.Equal(
            expected.Order(StringComparer.OrdinalIgnoreCase),
            actual.Order(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }
}
