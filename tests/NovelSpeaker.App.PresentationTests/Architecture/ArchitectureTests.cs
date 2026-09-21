using System.Text.RegularExpressions;
using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly ArchitectureTestRepository Repository = ArchitectureTestRepository.Locate();

    private void InfrastructurePublicTtsSourceApiDoesNotExposeJsonElement()
    {
        var jsonElementType = typeof(System.Text.Json.JsonElement);
        var exposedMembers = typeof(NovelSpeaker.Infrastructure.Speech.Legado.LegadoRuleConverter)
            .Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("NovelSpeaker.Infrastructure.Speech", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public |
                                                System.Reflection.BindingFlags.Instance |
                                                System.Reflection.BindingFlags.Static))
            .Where(method => method.ReturnType == jsonElementType ||
                             method.GetParameters().Any(parameter => parameter.ParameterType == jsonElementType))
            .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}")
            .ToArray();

        Assert.Empty(exposedMembers);
    }

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

    private void DomainContainsOnlyStableSpeechTypesAndNoTransportOrPersistenceModels()
    {
        var domainFiles = Repository.ReadProductSourceFiles()
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.Domain")
            .ToArray();
        var speechFiles = domainFiles
            .Where(file => file.RelativePath.StartsWith("src/NovelSpeaker.Domain/Speech/", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file.RelativePath))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["HttpTtsRule.cs", "TtsErrorKind.cs"], speechFiles);
        Assert.DoesNotContain(domainFiles, file =>
            file.Content.Contains("ParsedTtsRequest", StringComparison.Ordinal) ||
            file.Content.Contains("TtsRequestPreview", StringComparison.Ordinal) ||
            file.Content.Contains("ImportPreview", StringComparison.Ordinal) ||
            file.Content.Contains("RequestOptionsJson", StringComparison.Ordinal) ||
            file.Content.Contains("Sqlite", StringComparison.Ordinal));
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

    private void Playback_business_implementations_are_owned_by_Application()
    {
        var applicationAssembly = typeof(NovelSpeaker.Application.Playback.PlaybackCoordinator).Assembly;

        Assert.Equal(applicationAssembly, typeof(NovelSpeaker.Application.Playback.PlaybackCoordinator).Assembly);
        Assert.Equal(applicationAssembly, typeof(NovelSpeaker.Application.Playback.LocalAudioPlaybackCoordinator).Assembly);
        Assert.Equal(applicationAssembly, typeof(NovelSpeaker.Application.Playback.PlaybackContentResolver).Assembly);
        Assert.Equal(applicationAssembly, typeof(NovelSpeaker.Application.Playback.PlaybackPrefetchCoordinator).Assembly);
        Assert.Equal(applicationAssembly, typeof(NovelSpeaker.Application.Speech.Rules.SelectedTtsRuleProvider).Assembly);

        var infrastructurePlaybackFiles = Repository.ReadProductSourceFiles()
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.Infrastructure" &&
                           file.RelativePath.Contains("/Playback/", StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(
            infrastructurePlaybackFiles,
            file => file.Content.Contains("class PlaybackCoordinator", StringComparison.Ordinal) ||
                    file.Content.Contains("class LocalAudioPlaybackCoordinator", StringComparison.Ordinal) ||
                    file.Content.Contains("class PrefetchScheduler", StringComparison.Ordinal) ||
                    file.Content.Contains("class SelectedTtsRuleProvider", StringComparison.Ordinal));
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
            "src/NovelSpeaker.Application/Cache/CacheRegistration.cs",
            "src/NovelSpeaker.Application/Settings/SettingsRegistration.cs",
            "src/NovelSpeaker.Application/Observability/DependencyInjection/ObservabilityRegistration.cs",
            "src/NovelSpeaker.Infrastructure/DependencyInjection/AudioRegistration.cs",
            "src/NovelSpeaker.Infrastructure/DependencyInjection/CacheRegistration.cs",
            "src/NovelSpeaker.Infrastructure/DependencyInjection/DiagnosticsRegistration.cs",
            "src/NovelSpeaker.Infrastructure/DependencyInjection/SettingsRegistration.cs"
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

    private void LargeListHelpersDoNotClearThenAddOneItemAtATime()
    {
        var actual = ArchitectureRules.FindLargeListClearThenAddViolations(
            Repository.ReadProductSourceFiles(),
            [
                "src/NovelSpeaker.App/Shared/Presentation/ViewModelCollectionExtensions.cs",
                "src/NovelSpeaker.App/Shared/Presentation/ResettableObservableCollection.cs",
                "src/NovelSpeaker.App/Shared/Presentation/IndexedCatalog.cs",
                "src/NovelSpeaker.App/Features/Books/Details/BookDetailsViewModel.cs",
                "src/NovelSpeaker.App/Features/Cache/CacheManagementViewModel.cs",
                "src/NovelSpeaker.App/Features/Playback/Presentation/PlayerContentController.cs",
                "src/NovelSpeaker.App/Features/Playback/Presentation/PlayerViewModel.cs"
            ]);

        AssertEqualSet(KnownArchitectureBaseline.LargeListClearThenAddViolations, actual);
    }

    private void LibraryUsesStandardWpfRowVirtualization()
    {
        var libraryRoot = Path.Combine(
            Repository.RootPath,
            "src",
            "NovelSpeaker.App",
            "Features",
            "Books",
            "Library");
        Assert.False(File.Exists(Path.Combine(libraryRoot, "LibraryItemsControl.cs")));
        Assert.False(File.Exists(Path.Combine(libraryRoot, "LibraryResponsivePanel.cs")));

        var librarySourceFiles = Repository.ReadProductSourceFiles()
            .Where(file => file.RelativePath.StartsWith(
                "src/NovelSpeaker.App/Features/Books/Library/",
                StringComparison.Ordinal));
        Assert.All(
            librarySourceFiles,
            file =>
            {
                Assert.DoesNotContain("GenerateNext", file.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("IRecyclingItemContainerGenerator", file.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("IScrollInfo", file.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("realizedStart", file.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("realizedCount", file.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("InvalidateMeasure", file.Content, StringComparison.Ordinal);
            });

        var libraryPage = File.ReadAllText(Path.Combine(libraryRoot, "LibraryPage.xaml"));
        Assert.Contains("<ListBox", libraryPage, StringComparison.Ordinal);
        Assert.Contains("<VirtualizingStackPanel", libraryPage, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", libraryPage, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.ScrollUnit=\"Pixel\"", libraryPage, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", libraryPage, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.CanContentScroll=\"True\"", libraryPage, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", libraryPage, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"0\"", libraryPage, StringComparison.Ordinal);
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
        foreach (var propertyName in new[]
                 {
                     nameof(PlaybackSessionState.Book),
                     nameof(PlaybackSessionState.Rule),
                     nameof(PlaybackSessionState.ChapterIndex),
                     nameof(PlaybackSessionState.SegmentIndex),
                     nameof(PlaybackSessionState.SpeakSpeed),
                     nameof(PlaybackSessionState.ResumePositionMilliseconds),
                     nameof(PlaybackSessionState.ConsecutiveSegmentFailureCount),
                     nameof(PlaybackSessionState.CurrentAudio)
                 })
        {
            Assert.True(
                typeof(PlaybackSessionState).GetProperty(propertyName)!.SetMethod?.IsPrivate,
                $"PlaybackSessionState.{propertyName} must have a private setter.");
        }
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

    private static void PlayerPresentationControllersAreFeatureLocalConcreteTypes()
    {
        var controllerTypes = new[]
        {
            typeof(PlayerPlaybackProjection),
            typeof(PlayerContentController),
            typeof(PlayerSpeechControlController),
            typeof(PlayerCacheDecorationController),
            typeof(PlayerInteractionController)
        };

        Assert.All(controllerTypes, static type =>
        {
            Assert.False(type.IsPublic);
            Assert.True(type.IsSealed);
            Assert.False(type.IsInterface);
        });
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

    private void ProductionSourceFilesMatchNamespacesAndPrimaryPublicTypes()
    {
        var actual = ArchitectureRules.FindSourceLayoutViolations(Repository.ReadProductSourceFiles());

        AssertEqualSet(KnownArchitectureBaseline.SourceLayoutViolations, actual);
    }

    private void AppUsesFeatureSlicesInsteadOfGlobalUiDirectories()
    {
        var appRoot = Path.Combine(Repository.RootPath, "src", "NovelSpeaker.App");

        foreach (var legacyDirectory in new[] { "Pages", "Views", "ViewModels" })
        {
            Assert.False(
                Directory.Exists(Path.Combine(appRoot, legacyDirectory)),
                $"Legacy global UI directory still exists: {legacyDirectory}");
        }

        foreach (var feature in new[]
                 {
                     "Appearance",
                     "Books",
                     "Cache",
                     "Diagnostics",
                     "Playback",
                     "PlaybackSettings",
                     "Rules",
                     "Settings",
                 })
        {
            Assert.True(
                Directory.Exists(Path.Combine(appRoot, "Features", feature)),
                $"Feature slice directory is missing: {feature}");
        }

        foreach (var feature in new[]
                 {
                     "Books/Details",
                     "Books/Library",
                     "Books/Shared",
                     "Rules/Chapter",
                     "Rules/Regex",
                     "Rules/Shared",
                     "Rules/Tts"
                 })
        {
            Assert.True(
                Directory.Exists(Path.Combine(appRoot, "Features", feature)),
                $"Nested feature slice directory is missing: {feature}");
        }

        Assert.True(Directory.Exists(Path.Combine(appRoot, "Shared")));
        Assert.True(Directory.Exists(Path.Combine(appRoot, "Shell")));
    }

    private void AppKeepsOnlyReusableOrBehaviorOwningUserControlViews()
    {
        var appRoot = Path.Combine(Repository.RootPath, "src", "NovelSpeaker.App");
        var actual = Directory
            .EnumerateFiles(appRoot, "*View.xaml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(appRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Features/Books/Library/BookCardView.xaml",
                "Features/Books/Shared/BookCoverView.xaml",
                "Features/Playback/Components/PlayerView.xaml"
            ],
            actual);
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
        InfrastructurePublicTtsSourceApiDoesNotExposeJsonElement();
        DomainHasNoProductOrTechnicalDependencies();
        DomainContainsOnlyStableSpeechTypesAndNoTransportOrPersistenceModels();
        ApplicationOnlyHasDomainAndDocumentedDependencies();
        InfrastructureDoesNotDependOnAppOrWpf();
        ObservabilityApplicationApiDoesNotExposeInfrastructureTypes();
        Playback_business_implementations_are_owned_by_Application();
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
    public void Architecture_contracts_cover_optimization_phase_guards()
    {
        SharedPresentationDoesNotDependOnFeatures();
        FeatureNamespacesDoNotFormUnexpectedCycles();
        OrdinaryFeaturePagesAndViewModelsAreNotSingletons();
        FeaturePagesAndViewModelsDoNotUseServiceLocation();
        GenericGlobalCoordinationAbstractionsAreNotIntroduced();
        PagesAndViewModelsDoNotWriteReadingProgress();
        LargeListHelpersDoNotClearThenAddOneItemAtATime();
        LibraryUsesStandardWpfRowVirtualization();
        PlaybackStateHasOneOwnerAndReadOnlyConsumerContracts();
        PlayerPresentationControllersAreFeatureLocalConcreteTypes();
        ApplicationModulesHaveNoUnexpectedDependenciesOrCycles();
    }

    [Fact]
    public void Architecture_contracts_cover_source_layout_and_view_ownership()
    {
        ProductionSourceFilesMatchNamespacesAndPrimaryPublicTypes();
        AppUsesFeatureSlicesInsteadOfGlobalUiDirectories();
        AppKeepsOnlyReusableOrBehaviorOwningUserControlViews();
    }

    [Fact]
    public void Wpf_ui_scheduler_does_not_report_ordinary_dispatch_as_a_stall()
    {
        var scheduler = Repository.ReadProductSourceFiles().Single(file =>
            file.RelativePath == "src/NovelSpeaker.App/Shared/Presentation/Platform/WpfUiScheduler.cs");

        Assert.DoesNotContain("IObservability", scheduler.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("UiDispatcherStall", scheduler.Content, StringComparison.Ordinal);
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
