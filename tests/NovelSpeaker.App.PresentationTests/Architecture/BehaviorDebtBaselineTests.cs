using System.Xml.Linq;
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
    public void Rule_pages_register_the_global_navigation_guard_in_the_activation_scope()
    {
        var pagePaths = new[]
        {
            "src/NovelSpeaker.App/Features/ChapterRules/ChapterRulesPage.xaml.cs",
            "src/NovelSpeaker.App/Features/RegexReplacementRules/RegexReplacementRulesPage.xaml.cs",
            "src/NovelSpeaker.App/Features/TtsRules/TtsRulesPage.xaml.cs"
        };
        foreach (var relativePath in pagePaths)
        {
            var page = File.ReadAllText(Absolute(relativePath));
            Assert.Contains("INavigationGuardService", page, StringComparison.Ordinal);
            Assert.Contains("Register(ViewModel.ConfirmLeaveAsync)", page, StringComparison.Ordinal);
            Assert.Contains("PageActivationController", page, StringComparison.Ordinal);
            Assert.Contains("activation.Register", page, StringComparison.Ordinal);
            Assert.Contains("_activation.Deactivate()", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Async_event_pages_require_the_shared_exception_runner()
    {
        var pagePaths = new[]
        {
            "src/NovelSpeaker.App/Features/Cache/CacheAndDataPage.xaml.cs",
            "src/NovelSpeaker.App/Features/ChapterRules/ChapterRulesPage.xaml.cs",
            "src/NovelSpeaker.App/Features/ImportTextSettings/ImportTextSettingsPage.xaml.cs",
            "src/NovelSpeaker.App/Features/Library/LibraryPage.xaml.cs",
            "src/NovelSpeaker.App/Features/PlaybackSettings/PlaybackSettingsPage.xaml.cs",
            "src/NovelSpeaker.App/Features/RegexReplacementRules/RegexReplacementRulesPage.xaml.cs",
            "src/NovelSpeaker.App/Features/TtsRules/TtsRulesPage.xaml.cs"
        };

        foreach (var relativePath in pagePaths)
        {
            var page = File.ReadAllText(Absolute(relativePath));
            Assert.Contains("PageEventOperationRunner eventOperations", page, StringComparison.Ordinal);
            Assert.Contains("_eventOperations.RunAsync(", page, StringComparison.Ordinal);
            Assert.DoesNotContain("PageEventOperationRunner?", page, StringComparison.Ordinal);
            Assert.DoesNotContain("eventOperations = null", page, StringComparison.Ordinal);
            Assert.DoesNotContain("?? operation(", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Tts_rules_page_consumes_platform_ports_without_constructing_adapters()
    {
        var page = File.ReadAllText(Absolute("src/NovelSpeaker.App/Features/TtsRules/TtsRulesPage.xaml.cs"));

        Assert.Contains("IPresentationFileDialogService fileDialogs", page, StringComparison.Ordinal);
        Assert.Contains("IPresentationClipboard clipboard", page, StringComparison.Ordinal);
        Assert.DoesNotContain("new WpfPresentationFileDialogService", page, StringComparison.Ordinal);
        Assert.DoesNotContain("new WpfPresentationClipboard", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Win32", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Cache_export_uses_shared_folder_dialog_and_launcher_ports()
    {
        var viewModel = File.ReadAllText(
            Absolute("src/NovelSpeaker.App/Features/Cache/CacheManagementViewModel.cs"));
        var dialogPort = File.ReadAllText(
            Absolute("src/NovelSpeaker.App/Shared/Presentation/Platform/IPresentationFileDialogService.cs"));
        var dialogAdapter = File.ReadAllText(
            Absolute("src/NovelSpeaker.App/Shared/Presentation/Platform/WpfPresentationFileDialogService.cs"));

        Assert.Contains("IPresentationFileDialogService fileDialogs", viewModel, StringComparison.Ordinal);
        Assert.Contains("IPresentationLauncher launcher", viewModel, StringComparison.Ordinal);
        Assert.Contains("_fileDialogs.PickFolderAsync(", viewModel, StringComparison.Ordinal);
        Assert.Contains("_launcher.OpenAsync(", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Win32", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", viewModel, StringComparison.Ordinal);
        Assert.Contains("PickFolderAsync(", dialogPort, StringComparison.Ordinal);
        Assert.Contains("OpenFolderDialog", dialogAdapter, StringComparison.Ordinal);
    }

    [Fact]
    public void Tts_admission_and_settings_storage_do_not_synchronously_wait_for_async_work()
    {
        var limiter = File.ReadAllText(
            Absolute("src/NovelSpeaker.Infrastructure/Speech/Http/TtsRateLimiter.cs"));
        var settingsStore = File.ReadAllText(
            Absolute("src/NovelSpeaker.Infrastructure/Settings/JsonAppSettingsStore.cs"));

        Assert.DoesNotContain("SemaphoreSlim", limiter, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait(", limiter, StringComparison.Ordinal);
        Assert.DoesNotContain(".Result", limiter, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", limiter, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait(", settingsStore, StringComparison.Ordinal);
        Assert.DoesNotContain(".Result", settingsStore, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", settingsStore, StringComparison.Ordinal);
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
        var audioDirectory = Absolute("tests/NovelSpeaker.Infrastructure.IntegrationTests/TestAssets/Audio");
        var actual = Directory.EnumerateFiles(audioDirectory)
            .Select(path => Path.GetFileName(path)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AssertEqualSet(fixtureNames, actual);

        var appAudioDirectory = Absolute("src/NovelSpeaker.App/Assets/Audio");
        Assert.False(Directory.Exists(appAudioDirectory));

        var appProjectText = File.ReadAllText(Absolute("src/NovelSpeaker.App/NovelSpeaker.App.csproj"));
        Assert.DoesNotContain(@"Assets\Audio", appProjectText, StringComparison.OrdinalIgnoreCase);

        var testProject = XDocument.Load(Absolute("tests/NovelSpeaker.Infrastructure.IntegrationTests/NovelSpeaker.Infrastructure.IntegrationTests.csproj"));
        var testProjectText = File.ReadAllText(Absolute("tests/NovelSpeaker.Infrastructure.IntegrationTests/NovelSpeaker.Infrastructure.IntegrationTests.csproj"));
        Assert.DoesNotContain(@"src\NovelSpeaker.App\Assets\Audio", testProjectText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(testProject.Descendants("Content"), element =>
            string.Equals(element.Attribute("Include")?.Value, @"TestAssets\Audio\*.*", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_workflow_keeps_the_locked_quality_gate_and_package_validation()
    {
        var releaseWorkflow = File.ReadAllText(Absolute(".github/workflows/release.yml"));
        var qualityWorkflow = File.ReadAllText(Absolute(".github/workflows/quality-matrix.yml"));
        var releaseFragments = new[]
        {
            "uses: ./.github/workflows/quality-matrix.yml",
            "dotnet restore --locked-mode -r win-x64",
            "dotnet publish src/NovelSpeaker.App/NovelSpeaker.App.csproj -c Release -r win-x64 --self-contained true --no-restore",
            "NovelSpeaker.App.exe",
            "THIRD-PARTY-NOTICES.txt",
            "NAudio.dll",
            "NAudio.Wasapi.dll",
            "TestAssets",
            "demo-tone.wav",
            "demo-tone.mp3",
            "corrupt-tone.mp3",
            "Package contains test audio fixture",
            "Package contains test assembly"
        };
        var qualityFragments = new[]
        {
            "dotnet restore --locked-mode -r win-x64",
            "dotnet format --verify-no-changes --no-restore",
            "dotnet build -c Release --no-restore",
            "dotnet test ${{ matrix.project }} -c Release --no-build",
            "tests/NovelSpeaker.Domain.UnitTests/NovelSpeaker.Domain.UnitTests.csproj",
            "tests/NovelSpeaker.Infrastructure.IntegrationTests/NovelSpeaker.Infrastructure.IntegrationTests.csproj",
            "tests/NovelSpeaker.App.WpfTests/NovelSpeaker.App.WpfTests.csproj"
        };

        foreach (var fragment in releaseFragments)
        {
            Assert.Contains(fragment, releaseWorkflow, StringComparison.Ordinal);
        }

        foreach (var fragment in qualityFragments)
        {
            Assert.Contains(fragment, qualityWorkflow, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Shell_navigation_entry_points_use_the_typed_navigation_boundary()
    {
        var mainWindow = File.ReadAllText(Absolute("src/NovelSpeaker.App/Shell/MainWindow.xaml.cs"));
        Assert.Contains("OnRootNavigationViewNavigating", mainWindow, StringComparison.Ordinal);
        Assert.Contains("IShellActivationCoordinator", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_activationCoordinator.HandleNavigationRequestAsync", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(\"PageId\")", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper", mainWindow, StringComparison.Ordinal);

        var shellCoordinator = File.ReadAllText(
            Absolute("src/NovelSpeaker.App/Shell/Activation/ShellActivationCoordinator.cs"));
        Assert.Contains("IShellNavigationAdapter navigationAdapter", shellCoordinator, StringComparison.Ordinal);
        Assert.Contains("_navigationAdapter.NavigateFromShellAsync", shellCoordinator, StringComparison.Ordinal);
        var desktopExitGuard = File.ReadAllText(
            Absolute("src/NovelSpeaker.App/Desktop/Lifecycle/NavigationDesktopExitGuard.cs"));
        Assert.Contains("ConfirmNavigationAsync", desktopExitGuard, StringComparison.Ordinal);

        var shortcutContextResolver = File.ReadAllText(
            Absolute("src/NovelSpeaker.App/Shell/Input/WpfShortcutContextResolver.cs"));
        Assert.Contains("TextBoxBase", shortcutContextResolver, StringComparison.Ordinal);
        Assert.Contains("IsHostedInPopupSurface", shortcutContextResolver, StringComparison.Ordinal);
        Assert.Contains("FindVisibleContentDialog", shortcutContextResolver, StringComparison.Ordinal);

        var shortcuts = File.ReadAllText(Absolute("src/NovelSpeaker.App/Shell/Input/KeyboardShortcutCoordinator.cs"));
        Assert.Contains("IAppNavigator navigation", shortcuts, StringComparison.Ordinal);
        Assert.Contains("_navigation.GoBackAsync", shortcuts, StringComparison.Ordinal);
        Assert.Contains("AppRoutes.Settings", shortcuts, StringComparison.Ordinal);

        var shellViewModel = File.ReadAllText(Absolute("src/NovelSpeaker.App/Shell/MainWindowViewModel.cs"));
        Assert.Contains("IAppNavigator navigator", shellViewModel, StringComparison.Ordinal);
        Assert.Contains("_navigator.NavigateAsync", shellViewModel, StringComparison.Ordinal);
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
