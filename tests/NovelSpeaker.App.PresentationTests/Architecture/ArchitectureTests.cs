using System.Text.RegularExpressions;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly ArchitectureTestRepository Repository = ArchitectureTestRepository.Locate();

    [Fact]
    public void InfrastructurePublicTtsSourceApiDoesNotExposeJsonElement()
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

    [Fact]
    public void SolutionContainsExpectedProjects()
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
            "tests/NovelSpeaker.App.WpfTests/NovelSpeaker.App.WpfTests.csproj"
        };

        AssertEqualSet(expected, Repository.ReadSolutionProjectPaths());
    }

    [Fact]
    public void TestProjectsKeepTheirDocumentedResponsibilitiesAndReferenceBoundaries()
    {
        var expectedProjects = new[]
        {
            new TestProjectBoundary(
                "tests/NovelSpeaker.Domain.UnitTests/NovelSpeaker.Domain.UnitTests.csproj",
                "src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj",
                "net10.0",
                UsesWpf: false),
            new TestProjectBoundary(
                "tests/NovelSpeaker.Application.UnitTests/NovelSpeaker.Application.UnitTests.csproj",
                "src/NovelSpeaker.Application/NovelSpeaker.Application.csproj",
                "net10.0",
                UsesWpf: false),
            new TestProjectBoundary(
                "tests/NovelSpeaker.Infrastructure.IntegrationTests/NovelSpeaker.Infrastructure.IntegrationTests.csproj",
                "src/NovelSpeaker.Infrastructure/NovelSpeaker.Infrastructure.csproj",
                "net10.0",
                UsesWpf: false),
            new TestProjectBoundary(
                "tests/NovelSpeaker.App.PresentationTests/NovelSpeaker.App.PresentationTests.csproj",
                "src/NovelSpeaker.App/NovelSpeaker.App.csproj",
                "net10.0-windows10.0.19041.0",
                UsesWpf: false),
            new TestProjectBoundary(
                "tests/NovelSpeaker.App.WpfTests/NovelSpeaker.App.WpfTests.csproj",
                "src/NovelSpeaker.App/NovelSpeaker.App.csproj",
                "net10.0-windows10.0.19041.0",
                UsesWpf: true)
        };

        foreach (var expected in expectedProjects)
        {
            var project = Repository.ReadProject(expected.ProjectPath);

            AssertEqualSet([expected.ProductionProjectPath], project.ProjectReferences);
            Assert.Equal("true", project.Properties["IsTestProject"], ignoreCase: true);
            Assert.Equal(expected.TargetFramework, project.Properties["TargetFramework"]);
            Assert.Equal(expected.UsesWpf, ArchitectureRules.UsesWpf(project));
        }
    }

    [Fact]
    public void TestSourcesUseCurrentProjectAndSharedTestKitNamespaces()
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
        var protectedNamespaceExceptions = new HashSet<string>(StringComparer.Ordinal)
        {
            "tests/NovelSpeaker.App.WpfTests/WpfTestHost.cs",
            "tests/TestKit/Wpf/VisualTreeTestHelper.cs"
        };
        var violations = new List<string>();

        foreach (var (relativeDirectory, expectedRoot) in expectedRoots)
        {
            foreach (var file in ReadTestSourceFiles(relativeDirectory))
            {
                if (protectedNamespaceExceptions.Contains(file.RelativePath))
                {
                    continue;
                }

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

    [Fact]
    public void WpfSerializationDoesNotDisableParallelUnitTestProjects()
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

    [Fact]
    public void DomainHasNoProductOrTechnicalDependencies()
    {
        var project = Repository.ReadProject("src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj");

        Assert.Empty(project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
        Assert.Empty(project.FrameworkReferences);
        Assert.False(ArchitectureRules.UsesWpf(project));
    }

    [Fact]
    public void DomainContainsOnlyStableSpeechTypesAndNoTransportOrPersistenceModels()
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

    [Fact]
    public void ApplicationOnlyHasDomainAndDocumentedDependencies()
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

    [Fact]
    public void InfrastructureDoesNotDependOnAppOrWpf()
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

    [Fact]
    public void Playback_business_implementations_are_owned_by_Application()
    {
        var applicationAssembly = typeof(NovelSpeaker.Application.Playback.PlaybackCoordinator).Assembly;

        Assert.Equal(applicationAssembly, typeof(NovelSpeaker.Application.Playback.PlaybackCoordinator).Assembly);
        Assert.Equal(applicationAssembly, typeof(NovelSpeaker.Application.Playback.LocalAudioPlaybackCoordinator).Assembly);
        Assert.Equal(applicationAssembly, typeof(NovelSpeaker.Application.Playback.PlaybackPrefetchController).Assembly);
        Assert.Equal(applicationAssembly, typeof(NovelSpeaker.Application.Playback.SelectedTtsRuleProvider).Assembly);

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

    [Fact]
    public void AppOnlyUsesInfrastructureFromStartupCompositionBoundary()
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

    [Fact]
    public void ServiceProviderUsageStaysInsideCompositionAndFrameworkBridges()
    {
        var allowedRelativePaths = new[]
        {
            "src/NovelSpeaker.App/Bootstrap/WpfStartupRuntime.cs",
            "src/NovelSpeaker.App/Desktop/Lifecycle/DesktopLifecycleServiceCollectionExtensions.cs",
            "src/NovelSpeaker.App/Shell/Activation/WpfShellPlatformAdapter.cs",
            "src/NovelSpeaker.App/Shell/Navigation/AppNavigationPageProvider.cs",
            "src/NovelSpeaker.App/Shell/ShellServiceCollectionExtensions.cs",
            "src/NovelSpeaker.Application/Playback/PlaybackRegistration.cs",
            "src/NovelSpeaker.Application/Settings/SettingsRegistration.cs",
            "src/NovelSpeaker.Infrastructure/DependencyInjection/AudioRegistration.cs",
            "src/NovelSpeaker.Infrastructure/DependencyInjection/SettingsRegistration.cs"
        };

        var actual = ArchitectureRules.FindServiceLocationDependencies(
            Repository.ReadProductSourceFiles(),
            allowedRelativePaths);

        Assert.Empty(actual);
    }

    [Fact]
    public void AppDoesNotDirectlyDiscardAsyncOperations()
    {
        var appFiles = Repository.ReadProductSourceFiles()
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.App");

        var actual = ArchitectureRules.FindUnregisteredFireAndForgetOperations(appFiles);

        Assert.Empty(actual);
    }

    [Fact]
    public void ViewModelsDoNotAddWpfOrWpfUiTypesToPublicApi()
    {
        var actual = ArchitectureRules.FindForbiddenPublicApiDependencies(
            typeof(PlayerViewModel).Assembly,
            type => (type.Namespace?.StartsWith("NovelSpeaker.App.Features", StringComparison.Ordinal) == true ||
                     type.Namespace?.StartsWith("NovelSpeaker.App.Shell", StringComparison.Ordinal) == true) &&
                    type.Name.EndsWith("ViewModel", StringComparison.Ordinal));

        AssertEqualSet(KnownArchitectureBaseline.ViewModelForbiddenPublicApiDependencies, actual);
    }

    [Fact]
    public void ProductionSourceFilesMatchNamespacesAndPrimaryPublicTypes()
    {
        var actual = ArchitectureRules.FindSourceLayoutViolations(Repository.ReadProductSourceFiles());

        AssertEqualSet(KnownArchitectureBaseline.SourceLayoutViolations, actual);
    }

    [Fact]
    public void AppUsesFeatureSlicesInsteadOfGlobalUiDirectories()
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
                     "BookDetails",
                     "Cache",
                     "ChapterRules",
                     "Diagnostics",
                     "Library",
                     "Playback",
                     "PlaybackSettings",
                     "RegexReplacementRules",
                     "Settings",
                     "TtsRules"
                 })
        {
            Assert.True(
                Directory.Exists(Path.Combine(appRoot, "Features", feature)),
                $"Feature slice directory is missing: {feature}");
        }

        Assert.True(Directory.Exists(Path.Combine(appRoot, "Shared")));
        Assert.True(Directory.Exists(Path.Combine(appRoot, "Shell")));
    }

    [Fact]
    public void AppKeepsOnlyReusableOrBehaviorOwningUserControlViews()
    {
        var appRoot = Path.Combine(Repository.RootPath, "src", "NovelSpeaker.App");
        var actual = Directory
            .EnumerateFiles(appRoot, "*View.xaml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(appRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Features/Library/BookCardView.xaml",
                "Features/Playback/Components/PlayerView.xaml",
                "Shared/Presentation/Books/BookCoverView.xaml"
            ],
            actual);
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
        string ProductionProjectPath,
        string TargetFramework,
        bool UsesWpf);

    private sealed record TestSourceFile(string RelativePath, string Content);
}
