using System.Text.RegularExpressions;
using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly ArchitectureTestRepository Repository = ArchitectureTestRepository.Locate();

    private void SolutionContainsExpectedProjects()
    {
        var expected = new[]
        {
            "src/NovelSpeaker.App/NovelSpeaker.App.csproj",
            "src/NovelSpeaker.Application/NovelSpeaker.Application.csproj",
            "src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj",
            "src/NovelSpeaker.Infrastructure/NovelSpeaker.Infrastructure.csproj",
            "tests/NovelSpeaker.Domain.UnitTests/NovelSpeaker.Domain.UnitTests.csproj",
            "tests/NovelSpeaker.Application.UnitTests/NovelSpeaker.Application.UnitTests.csproj",
            "tests/NovelSpeaker.Infrastructure.IntegrationTests/NovelSpeaker.Infrastructure.IntegrationTests.csproj",
            "tests/NovelSpeaker.App.PresentationTests/NovelSpeaker.App.PresentationTests.csproj",
            "tests/NovelSpeaker.App.WpfTests/NovelSpeaker.App.WpfTests.csproj",
            "tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj"
        };

        AssertEqualSet(expected, Repository.ReadSolutionProjectPaths());
    }

    private void TestProjectsKeepTheirDocumentedResponsibilitiesAndReferenceBoundaries()
    {
        var expectedProjects = new[]
        {
            new TestProjectBoundary(
                "tests/NovelSpeaker.Domain.UnitTests/NovelSpeaker.Domain.UnitTests.csproj",
                ["src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj"],
                "net10.0",
                UsesWpf: false),
            new TestProjectBoundary(
                "tests/NovelSpeaker.Application.UnitTests/NovelSpeaker.Application.UnitTests.csproj",
                ["src/NovelSpeaker.Application/NovelSpeaker.Application.csproj"],
                "net10.0",
                UsesWpf: false),
            new TestProjectBoundary(
                "tests/NovelSpeaker.Infrastructure.IntegrationTests/NovelSpeaker.Infrastructure.IntegrationTests.csproj",
                ["src/NovelSpeaker.Infrastructure/NovelSpeaker.Infrastructure.csproj"],
                "net10.0",
                UsesWpf: false),
            new TestProjectBoundary(
                "tests/NovelSpeaker.App.PresentationTests/NovelSpeaker.App.PresentationTests.csproj",
                ["src/NovelSpeaker.App/NovelSpeaker.App.csproj"],
                "net10.0-windows10.0.19041.0",
                UsesWpf: false),
            new TestProjectBoundary(
                "tests/NovelSpeaker.App.WpfTests/NovelSpeaker.App.WpfTests.csproj",
                [
                    "src/NovelSpeaker.App/NovelSpeaker.App.csproj",
                    "tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj"
                ],
                "net10.0-windows10.0.19041.0",
                UsesWpf: true)
        };

        foreach (var expected in expectedProjects)
        {
            var project = Repository.ReadProject(expected.ProjectPath);

            AssertEqualSet(expected.ProductionProjectPaths, project.ProjectReferences);
            Assert.Equal("true", project.Properties["IsTestProject"], ignoreCase: true);
            Assert.Equal(expected.TargetFramework, project.Properties["TargetFramework"]);
            Assert.Equal(expected.UsesWpf, ArchitectureRules.UsesWpf(project));
        }
    }

    private void Style_gallery_uses_shared_app_resources_without_reverse_dependency_or_data_layers()
    {
        var gallery = Repository.ReadProject("tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj");
        AssertEqualSet(["src/NovelSpeaker.App/NovelSpeaker.App.csproj"], gallery.ProjectReferences);
        Assert.Empty(gallery.FrameworkReferences);
        AssertEqualSet(["wpf-ui"], gallery.PackageReferences);
        Assert.Equal("false", gallery.Properties["IsPackable"], ignoreCase: true);

        Assert.DoesNotContain(
            gallery.ProjectReferences,
            reference => reference is
                "src/NovelSpeaker.Application/NovelSpeaker.Application.csproj" or
                "src/NovelSpeaker.Infrastructure/NovelSpeaker.Infrastructure.csproj");

        var app = Repository.ReadProject("src/NovelSpeaker.App/NovelSpeaker.App.csproj");
        Assert.DoesNotContain(
            app.ProjectReferences,
            reference => reference.Equals(
                "tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj",
                StringComparison.Ordinal));

        var galleryRoot = Path.Combine(Repository.RootPath, "tools", "NovelSpeaker.StyleGallery");
        var forbiddenFragments = new[]
        {
            "NovelSpeaker.Infrastructure",
            "Microsoft.Data.Sqlite",
            "settings.json",
            "UserData",
            "Cache"
        };
        var gallerySources = Directory.EnumerateFiles(galleryRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(galleryRoot, "*.xaml", SearchOption.AllDirectories))
            .Select(File.ReadAllText)
            .ToArray();
        Assert.DoesNotContain(gallerySources, source =>
            forbiddenFragments.Any(fragment => source.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    private void TestSourcesUseCurrentProjectAndSharedTestKitNamespaces()
    {
        var expectedRoots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tests/NovelSpeaker.Domain.UnitTests"] = "NovelSpeaker.Domain.UnitTests",
            ["tests/NovelSpeaker.Application.UnitTests"] = "NovelSpeaker.Application.UnitTests",
            ["tests/NovelSpeaker.Infrastructure.IntegrationTests"] = "NovelSpeaker.Infrastructure.IntegrationTests",
            ["tests/NovelSpeaker.App.PresentationTests"] = "NovelSpeaker.App.PresentationTests",
            ["tests/NovelSpeaker.App.WpfTests"] = "NovelSpeaker.App.WpfTests",
            ["tests/TestKit"] = "NovelSpeaker.TestKit"
        };
        var violations = new List<string>();

        foreach (var (relativeDirectory, expectedRoot) in expectedRoots)
        {
            foreach (var file in ReadTestSourceFiles(relativeDirectory))
            {
                var match = Regex.Match(
                    file.Content,
                    @"^\s*namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*[;{]",
                    RegexOptions.Multiline | RegexOptions.CultureInvariant);
                if (match.Success &&
                    !match.Groups["name"].Value.Equals(expectedRoot, StringComparison.Ordinal) &&
                    !match.Groups["name"].Value.StartsWith(expectedRoot + ".", StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{file.RelativePath}: namespace '{match.Groups["name"].Value}', expected root '{expectedRoot}'");
                }
            }
        }

        Assert.Empty(violations);
    }

    private void WpfSerializationDoesNotDisableParallelUnitTestProjects()
    {
        var globallySerializedProjects = new[]
        {
            "tests/NovelSpeaker.Domain.UnitTests",
            "tests/NovelSpeaker.Application.UnitTests",
            "tests/NovelSpeaker.Infrastructure.IntegrationTests",
            "tests/NovelSpeaker.App.PresentationTests"
        };

        foreach (var projectDirectory in globallySerializedProjects)
        {
            var sources = ReadParallelPolicySourceFiles(projectDirectory);
            Assert.DoesNotContain(
                sources,
                file => file.Content.Contains("DisableTestParallelization", StringComparison.Ordinal) ||
                        file.Content.Contains("DisableParallelization = true", StringComparison.Ordinal));
            Assert.False(File.Exists(Path.Combine(
                Repository.RootPath,
                projectDirectory.Replace('/', Path.DirectorySeparatorChar),
                "xunit.runner.json")));
        }

        var wpfSources = ReadParallelPolicySourceFiles("tests/NovelSpeaker.App.WpfTests");
        Assert.DoesNotContain(
            wpfSources,
            file => file.Content.Contains("DisableTestParallelization", StringComparison.Ordinal));
        var collectionDefinitions = wpfSources
            .Where(file => file.Content.Contains(
                "[CollectionDefinition(\"WpfDispatcher\", DisableParallelization = true)]",
                StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToArray();

        Assert.Equal(["tests/NovelSpeaker.App.WpfTests/WpfTestCollection.cs"], collectionDefinitions);
    }

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
        Assert.Empty(ArchitectureRules.FindPlaybackSessionStateMutationViolations(
            Repository.ReadProductSourceFiles()));
        Assert.False(typeof(PlaybackSessionState).IsPublic);
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
    public void Architecture_contracts_cover_solution_projects_and_project_boundaries()
    {
        SolutionContainsExpectedProjects();
        TestProjectsKeepTheirDocumentedResponsibilitiesAndReferenceBoundaries();
        Style_gallery_uses_shared_app_resources_without_reverse_dependency_or_data_layers();
    }

    [Fact]
    public void Architecture_contracts_cover_test_source_and_parallelism_boundaries()
    {
        TestSourcesUseCurrentProjectAndSharedTestKitNamespaces();
        WpfSerializationDoesNotDisableParallelUnitTestProjects();
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

    private static IReadOnlyList<TestSourceFile> ReadTestSourceFiles(string relativeDirectory)
    {
        var directory = Path.Combine(
            Repository.RootPath,
            relativeDirectory.Replace('/', Path.DirectorySeparatorChar));

        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Select(path => new TestSourceFile(
                Repository.ToRepositoryRelativePath(path),
                File.ReadAllText(path)))
            .Where(file => !file.RelativePath.Contains("/bin/", StringComparison.Ordinal) &&
                           !file.RelativePath.Contains("/obj/", StringComparison.Ordinal))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<TestSourceFile> ReadParallelPolicySourceFiles(string relativeDirectory) =>
        ReadTestSourceFiles(relativeDirectory)
            .Where(file => !file.RelativePath.EndsWith(
                "/Architecture/ArchitectureTests.cs",
                StringComparison.Ordinal))
            .ToArray();

    private sealed record TestProjectBoundary(
        string ProjectPath,
        IReadOnlyList<string> ProductionProjectPaths,
        string TargetFramework,
        bool UsesWpf);

    private sealed record TestSourceFile(string RelativePath, string Content);
}
