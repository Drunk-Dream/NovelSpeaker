using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly ArchitectureTestRepository Repository = ArchitectureTestRepository.Locate();

    private void DomainHasNoProductOrTechnicalDependencies()
    {
        var project = Repository.ReadProject("src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj");

        Assert.Empty(project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
        Assert.Empty(project.FrameworkReferences);
        Assert.False(ArchitectureRules.UsesWpf(project));
    }

    private void ApplicationOnlyHasDomainAndDocumentedDependencies()
    {
        var project = Repository.ReadProject("src/NovelSpeaker.Application/NovelSpeaker.Application.csproj");

        AssertEqualSet(
            ["src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj"],
            project.ProjectReferences);
        AssertEqualSet(
            ["Microsoft.Extensions.DependencyInjection.Abstractions"],
            project.PackageReferences);
        Assert.DoesNotContain(
            project.PackageReferences,
            package => package.Equals("Microsoft.Data.Sqlite.Core", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(project.FrameworkReferences);
        Assert.False(ArchitectureRules.UsesWpf(project));

        var files = Repository.ReadProductSourceFiles()
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.Application")
            .ToArray();
        Assert.DoesNotContain(
            files,
            file => Path.GetFileName(file.RelativePath)
                .Equals("ISqliteConnectionFactory.cs", StringComparison.Ordinal));

        var actual = ArchitectureRules.FindForbiddenSourceDependencies(
            files,
            [
                "Microsoft.Data.Sqlite",
                "Jint",
                "NAudio",
                "System.Windows",
                "Wpf.Ui",
                "NovelSpeaker.Infrastructure"
            ]);

        Assert.Empty(actual);
    }

    private void ObservabilityApplicationApiDoesNotExposeInfrastructureTypes()
    {
        var observabilityFiles = Repository.ReadProductSourceFiles()
            .Where(file => file.RelativePath.StartsWith(
                "src/NovelSpeaker.Application/Observability/",
                StringComparison.Ordinal));

        Assert.Empty(ArchitectureRules.FindForbiddenSourceDependencies(
            observabilityFiles,
            [
                "Microsoft.Data.Sqlite",
                "Microsoft.Extensions.Logging",
                "NovelSpeaker.Infrastructure",
                "System.IO.Compression"
            ]));
    }

    private void InfrastructureDoesNotDependOnAppOrWpf()
    {
        var project = Repository.ReadProject("src/NovelSpeaker.Infrastructure/NovelSpeaker.Infrastructure.csproj");

        AssertEqualSet(
            [
                "src/NovelSpeaker.Application/NovelSpeaker.Application.csproj",
                "src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj"
            ],
            project.ProjectReferences);
        Assert.DoesNotContain(project.PackageReferences, package =>
            package.Equals("wpf-ui", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(project.FrameworkReferences, reference =>
            reference.Contains("WindowsDesktop", StringComparison.OrdinalIgnoreCase));
        Assert.False(ArchitectureRules.UsesWpf(project));

        var files = Repository.ReadProductSourceFiles()
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.Infrastructure");
        var actual = ArchitectureRules.FindForbiddenSourceDependencies(
            files,
            ["NovelSpeaker.App", "System.Windows", "Wpf.Ui"]);

        Assert.Empty(actual);
    }

    private void AppOnlyUsesInfrastructureFromStartupCompositionBoundary()
    {
        var project = Repository.ReadProject("src/NovelSpeaker.App/NovelSpeaker.App.csproj");

        AssertEqualSet(
            [
                "src/NovelSpeaker.Application/NovelSpeaker.Application.csproj",
                "src/NovelSpeaker.Infrastructure/NovelSpeaker.Infrastructure.csproj"
            ],
            project.ProjectReferences);

        var files = Repository.ReadProductSourceFiles()
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.App");
        var actual = ArchitectureRules.FindAppInfrastructureDependencies(files);

        AssertEqualSet(KnownArchitectureBaseline.AppInfrastructureSourceFiles, actual);
    }

    private void ServiceProviderUsageStaysInsideCompositionAndFrameworkBridges()
    {
        var allowedRelativePaths = new[]
        {
            "src/NovelSpeaker.App/Bootstrap/WpfStartupRuntime.cs",
            "src/NovelSpeaker.App/Desktop/Lifecycle/DesktopLifecycleServiceCollectionExtensions.cs",
            "src/NovelSpeaker.App/Shell/Activation/WpfShellPlatformAdapter.cs",
            "src/NovelSpeaker.App/Shell/Navigation/AppNavigationPageProvider.cs",
            "src/NovelSpeaker.App/Shell/ShellServiceCollectionExtensions.cs",
            "src/NovelSpeaker.Application/Playback/PlaybackRegistration.cs",
            "src/NovelSpeaker.Application/Books/BooksRegistration.cs",
            "src/NovelSpeaker.Application/Cache/CacheRegistration.cs",
            "src/NovelSpeaker.Application/Settings/SettingsRegistration.cs",
            "src/NovelSpeaker.Application/Speech/SpeechRegistration.cs",
            "src/NovelSpeaker.Application/Observability/DependencyInjection/ObservabilityRegistration.cs",
            "src/NovelSpeaker.Infrastructure/DependencyInjection/AudioRegistration.cs",
            "src/NovelSpeaker.Infrastructure/DependencyInjection/CacheRegistration.cs",
            "src/NovelSpeaker.Infrastructure/DependencyInjection/DiagnosticsRegistration.cs",
            "src/NovelSpeaker.Infrastructure/DependencyInjection/SettingsRegistration.cs",
            "src/NovelSpeaker.Infrastructure/DependencyInjection/SpeechRegistration.cs"
        };

        var actual = ArchitectureRules.FindServiceLocationDependencies(
            Repository.ReadProductSourceFiles(),
            allowedRelativePaths);

        Assert.Empty(actual);
    }

    private void SharedPresentationDoesNotDependOnFeatures()
    {
        var sharedFiles = Repository.ReadProductSourceFiles()
            .Concat(Repository.ReadProductXamlFiles())
            .Where(file => file.RelativePath.Contains(
                "src/NovelSpeaker.App/Shared/",
                StringComparison.Ordinal));
        var actual = ArchitectureRules.FindSharedFeatureDependencies(sharedFiles);

        AssertEqualSet(KnownArchitectureBaseline.SharedFeatureSourceDependencies, actual);
    }

    private void FeatureNamespacesDoNotFormUnexpectedCycles()
    {
        var featureFiles = Repository.ReadProductSourceFiles()
            .Concat(Repository.ReadProductXamlFiles())
            .Where(file => file.RelativePath.Contains(
                "src/NovelSpeaker.App/Features/",
                StringComparison.Ordinal));
        var actual = ArchitectureRules.FindFeatureDependencyCycles(featureFiles);

        AssertEqualSet(KnownArchitectureBaseline.FeatureDependencyCycles, actual);
    }

    private void OrdinaryFeaturePagesAndViewModelsAreNotSingletons()
    {
        var actual = ArchitectureRules.FindSingletonFeaturePageOrViewModelRegistrations(
            Repository.ReadProductSourceFiles());

        AssertEqualSet(KnownArchitectureBaseline.FeaturePageOrViewModelSingletonRegistrations, actual);
    }

    private void FeaturePagesAndViewModelsDoNotUseServiceLocation()
    {
        var featureFiles = Repository.ReadProductSourceFiles()
            .Where(file => file.RelativePath.Contains(
                "src/NovelSpeaker.App/Features/",
                StringComparison.Ordinal));
        var actual = ArchitectureRules.FindServiceLocationDependencies(featureFiles, []);

        Assert.Empty(actual);
    }

    private void GenericGlobalCoordinationAbstractionsAreNotIntroduced()
    {
        var actual = ArchitectureRules.FindForbiddenGenericAbstractionDeclarations(
            Repository.ReadProductSourceFiles());

        Assert.Empty(actual);
    }

    private void PagesAndViewModelsDoNotWriteReadingProgress()
    {
        var actual = ArchitectureRules.FindReadingProgressWriterDependencies(
            Repository.ReadProductSourceFiles());

        Assert.Empty(actual);
    }

    private void PlaybackStateHasOneOwnerAndReadOnlyConsumerContracts()
    {
        var appFiles = Repository.ReadProductSourceFiles()
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.App");
        var actual = ArchitectureRules.FindConcretePlaybackCoordinatorDependencies(
            appFiles,
            ["src/NovelSpeaker.App/Bootstrap/WpfStartupRuntime.cs"]);

        Assert.Empty(actual);
        Assert.Empty(ArchitectureRules.FindPlaybackConsumerBoundaryViolations(
            appFiles,
            [
                "src/NovelSpeaker.App/Desktop/Lifecycle/DesktopLifecycleCoordinator.cs",
                "src/NovelSpeaker.App/Desktop/MiniPlayer/MiniPlayerViewModel.cs",
                "src/NovelSpeaker.App/Features/Books/Details/BookDetailsViewModel.cs",
                "src/NovelSpeaker.App/Features/Books/Library/LibraryViewModel.cs",
                "src/NovelSpeaker.App/Features/Playback/Presentation/PlayerViewModel.cs",
                "src/NovelSpeaker.App/Features/Playback/Presentation/PlayerInteractionController.cs",
                "src/NovelSpeaker.App/Features/Playback/Presentation/PlayerSpeechControlController.cs",
                "src/NovelSpeaker.App/Features/PlaybackSettings/PlaybackSettingsViewModel.cs",
                "src/NovelSpeaker.App/Shell/MainWindowViewModel.cs"
            ],
            [
                "src/NovelSpeaker.App/Features/Playback/Presentation/PlayerContentController.cs",
                "src/NovelSpeaker.App/Features/Playback/Presentation/PlayerPlaybackProjection.cs",
                "src/NovelSpeaker.App/Features/Books/Details/BookDetailsProjectionController.cs",
                "src/NovelSpeaker.App/Features/Books/Shared/EffectiveReadingProgress.cs"
            ]));

        Assert.Equal(
            ["src/NovelSpeaker.Application/Playback/PlaybackRegistration.cs: Singleton"],
            ArchitectureRules.FindPlaybackCoordinatorRegistrations(Repository.ReadProductSourceFiles()));
        Assert.False(typeof(PlaybackRuntime).IsPublic);
        Assert.False(typeof(PlaybackRuntimeState).IsPublic);
        var runtimeState = typeof(PlaybackRuntime).GetProperty(nameof(PlaybackRuntime.Current))!;
        Assert.True(runtimeState.GetSetMethod(nonPublic: true)!.IsPrivate);
        Assert.Equal(typeof(PlaybackSnapshot), typeof(IPlaybackSnapshotSource)
            .GetProperty(nameof(IPlaybackSnapshotSource.CurrentSnapshot))!.PropertyType);
        Assert.Null(typeof(IPlaybackSnapshotSource)
            .GetProperty(nameof(IPlaybackSnapshotSource.CurrentSnapshot))!.SetMethod);
        Assert.Equal(
            [typeof(PlaybackCoordinator)],
            new[]
            {
                typeof(PlaybackCoordinator).Assembly,
                typeof(NovelSpeaker.App.Features.Playback.Presentation.PlayerViewModel).Assembly,
                typeof(NovelSpeaker.Domain.Books.Book).Assembly,
                typeof(NovelSpeaker.Infrastructure.Persistence.SqliteReadingProgressStore).Assembly
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IPlaybackSession).IsAssignableFrom(type) &&
                           !type.IsInterface)
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray());
    }

    private void ApplicationModulesHaveNoUnexpectedDependenciesOrCycles()
    {
        var applicationFiles = Repository.ReadProductSourceFiles()
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.Application")
            .ToArray();

        var dependencyViolations = ArchitectureRules.FindApplicationModuleDependencyViolations(applicationFiles);
        Assert.True(
            dependencyViolations.Count == 0,
            string.Join(Environment.NewLine, dependencyViolations));

        var cycleViolations = ArchitectureRules.FindApplicationModuleDependencyCycles(applicationFiles);
        Assert.True(
            cycleViolations.Count == 0,
            string.Join(Environment.NewLine, cycleViolations));

        var mutableTruthViolations = ArchitectureRules.FindApplicationModuleMutableTruthViolations(applicationFiles);
        Assert.True(
            mutableTruthViolations.Count == 0,
            string.Join(Environment.NewLine, mutableTruthViolations));

        Assert.Equal(
            Enum.GetValues<ApplicationModule>().Order().ToArray(),
            ArchitectureRules.FindApplicationModules(applicationFiles).Order().ToArray());
    }

    private void AppDoesNotDirectlyDiscardAsyncOperations()
    {
        var appFiles = Repository.ReadProductSourceFiles()
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.App");

        var actual = ArchitectureRules.FindUnregisteredFireAndForgetOperations(appFiles);

        Assert.Empty(actual);
    }

    private void ViewModelsDoNotAddWpfOrWpfUiTypesToPublicApi()
    {
        var actual = ArchitectureRules.FindForbiddenPublicApiDependencies(
            typeof(PlayerViewModel).Assembly,
            type => (type.Namespace?.StartsWith("NovelSpeaker.App.Features", StringComparison.Ordinal) == true ||
                     type.Namespace?.StartsWith("NovelSpeaker.App.Shell", StringComparison.Ordinal) == true) &&
                    type.Name.EndsWith("ViewModel", StringComparison.Ordinal));

        AssertEqualSet(KnownArchitectureBaseline.ViewModelForbiddenPublicApiDependencies, actual);
    }

    [Fact]
    public void Architecture_contracts_cover_layer_dependencies_and_ownership()
    {
        DomainHasNoProductOrTechnicalDependencies();
        ApplicationOnlyHasDomainAndDocumentedDependencies();
        InfrastructureDoesNotDependOnAppOrWpf();
        ObservabilityApplicationApiDoesNotExposeInfrastructureTypes();
        AppOnlyUsesInfrastructureFromStartupCompositionBoundary();
        ServiceProviderUsageStaysInsideCompositionAndFrameworkBridges();
    }

    [Fact]
    public void Architecture_contracts_cover_async_and_public_api_boundaries()
    {
        AppDoesNotDirectlyDiscardAsyncOperations();
        ViewModelsDoNotAddWpfOrWpfUiTypesToPublicApi();
    }

    [Fact]
    public void Architecture_contracts_cover_module_dependencies_and_state_ownership()
    {
        SharedPresentationDoesNotDependOnFeatures();
        FeatureNamespacesDoNotFormUnexpectedCycles();
        OrdinaryFeaturePagesAndViewModelsAreNotSingletons();
        FeaturePagesAndViewModelsDoNotUseServiceLocation();
        GenericGlobalCoordinationAbstractionsAreNotIntroduced();
        PagesAndViewModelsDoNotWriteReadingProgress();
        PlaybackStateHasOneOwnerAndReadOnlyConsumerContracts();
        ApplicationModulesHaveNoUnexpectedDependenciesOrCycles();
    }

    private static void AssertEqualSet(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var expectedArray = expected.Order(StringComparer.Ordinal).ToArray();
        var actualArray = actual.Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            expectedArray.SequenceEqual(actualArray, StringComparer.Ordinal),
            $"Expected:{Environment.NewLine}{string.Join(Environment.NewLine, expectedArray)}" +
            $"{Environment.NewLine}Actual:{Environment.NewLine}{string.Join(Environment.NewLine, actualArray)}");
    }

}
