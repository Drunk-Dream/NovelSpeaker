using System.Windows;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class ArchitectureRuleContractTests
{
    [Fact]
    public void ForbiddenSourceDependencyRuleRejectsAddedDependency()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.Application/Playback/InvalidService.cs",
                "src/NovelSpeaker.Application",
                "using Jint; namespace NovelSpeaker.Application.Playback; public sealed class InvalidService;")
        };

        var violations = ArchitectureRules.FindForbiddenSourceDependencies(files, ["Jint"]);

        Assert.Equal(
            ["src/NovelSpeaker.Application/Playback/InvalidService.cs -> Jint"],
            violations);
    }

    [Fact]
    public void AppInfrastructureRuleRejectsDependencyOutsideCompositionBoundary()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/ViewModels/InvalidViewModel.cs",
                "src/NovelSpeaker.App",
                "using NovelSpeaker.Infrastructure.Playback; namespace NovelSpeaker.App.ViewModels; public sealed class InvalidViewModel;")
        };

        var violations = ArchitectureRules.FindAppInfrastructureDependencies(files);

        Assert.Equal(["src/NovelSpeaker.App/ViewModels/InvalidViewModel.cs"], violations);
    }

    [Fact]
    public void SourceDependencyRulesIgnoreCommentsAndStringLiterals()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/ViewModels/SafeViewModel.cs",
                "src/NovelSpeaker.App",
                "// NovelSpeaker.Infrastructure.Playback\nnamespace NovelSpeaker.App.ViewModels; public sealed class SafeViewModel { public string Text => \"NovelSpeaker.Infrastructure.Playback\"; }")
        };

        Assert.Empty(ArchitectureRules.FindAppInfrastructureDependencies(files));
    }

    [Fact]
    public void ServiceLocationRuleRejectsProviderUsageOutsideAllowedBoundaries()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Features/InvalidViewModel.cs",
                "src/NovelSpeaker.App",
                "namespace NovelSpeaker.App.Features; public sealed class InvalidViewModel(IServiceProvider services, IKeyedServiceProvider keyed) { public object Resolve() => services.GetRequiredService<object>(); public object ResolveMany() => services.GetServices<object>().First(); public object ResolveKeyed() => keyed.GetKeyedService<object>(\"key\")!; public object ResolveKeyedRequired() => keyed.GetRequiredKeyedService<object>(\"key\"); public object ResolveKeyedRequiredMany() => keyed.GetRequiredKeyedServices<object>(\"key\").First(); public IServiceProvider Build() => services.BuildServiceProvider(); }"),
            Source(
                "src/NovelSpeaker.App/Bootstrap/AllowedComposition.cs",
                "src/NovelSpeaker.App",
                "namespace NovelSpeaker.App.Bootstrap; public sealed class AllowedComposition(IServiceProvider services);")
        };

        var violations = ArchitectureRules.FindServiceLocationDependencies(
            files,
            ["src/NovelSpeaker.App/Bootstrap/AllowedComposition.cs"]);

        Assert.Equal(["src/NovelSpeaker.App/Features/InvalidViewModel.cs"], violations);
    }

    [Fact]
    public void FeatureDependencyRuleRejectsAFeatureCycle()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Features/Alpha/AlphaViewModel.cs",
                "src/NovelSpeaker.App",
                "using NovelSpeaker.App.Features.Beta; namespace NovelSpeaker.App.Features.Alpha; public sealed class AlphaViewModel;"),
            Source(
                "src/NovelSpeaker.App/Features/Beta/BetaViewModel.cs",
                "src/NovelSpeaker.App",
                "using NovelSpeaker.App.Features.Alpha; namespace NovelSpeaker.App.Features.Beta; public sealed class BetaViewModel;")
        };

        Assert.Equal(
            [
                "src/NovelSpeaker.App/Features/Alpha/AlphaViewModel.cs -> NovelSpeaker.App.Features.Beta",
                "src/NovelSpeaker.App/Features/Beta/BetaViewModel.cs -> NovelSpeaker.App.Features.Alpha"
            ],
            ArchitectureRules.FindFeatureDependencyCycles(files));
    }

    [Fact]
    public void FeatureDependencyRulePreservesNestedBooksFeatureBoundaries()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Features/Books/Library/LibraryViewModel.cs",
                "src/NovelSpeaker.App",
                "using NovelSpeaker.App.Features.Books.Details; namespace NovelSpeaker.App.Features.Books.Library; public sealed class LibraryViewModel;"),
            Source(
                "src/NovelSpeaker.App/Features/Books/Details/BookDetailsViewModel.cs",
                "src/NovelSpeaker.App",
                "using NovelSpeaker.App.Features.Books.Library; namespace NovelSpeaker.App.Features.Books.Details; public sealed class BookDetailsViewModel;")
        };

        Assert.Equal(
            [
                "src/NovelSpeaker.App/Features/Books/Details/BookDetailsViewModel.cs -> NovelSpeaker.App.Features.Books.Library",
                "src/NovelSpeaker.App/Features/Books/Library/LibraryViewModel.cs -> NovelSpeaker.App.Features.Books.Details"
            ],
            ArchitectureRules.FindFeatureDependencyCycles(files));
    }

    [Fact]
    public void FeatureDependencyRuleRejectsAResourceDictionaryCycleWithoutXamlClass()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Features/Alpha/Resources.xaml",
                "src/NovelSpeaker.App",
                "<ResourceDictionary xmlns:beta=\"clr-namespace:NovelSpeaker.App.Features.Beta\" />"),
            Source(
                "src/NovelSpeaker.App/Features/Beta/Resources.xaml",
                "src/NovelSpeaker.App",
                "<ResourceDictionary xmlns:alpha=\"clr-namespace:NovelSpeaker.App.Features.Alpha\" />")
        };

        Assert.Equal(
            [
                "src/NovelSpeaker.App/Features/Alpha/Resources.xaml -> NovelSpeaker.App.Features.Beta",
                "src/NovelSpeaker.App/Features/Beta/Resources.xaml -> NovelSpeaker.App.Features.Alpha"
            ],
            ArchitectureRules.FindFeatureDependencyCycles(files));
    }

    [Fact]
    public void FeatureDependencyRuleIgnoresXamlCommentsWhenIdentifyingTheSourceFeature()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Features/Alpha/Resources.xaml",
                "src/NovelSpeaker.App",
                "<!-- x:Class=\"NovelSpeaker.App.Features.Beta.CommentOnly\" --><ResourceDictionary xmlns:beta=\"clr-namespace:NovelSpeaker.App.Features.Beta\" />"),
            Source(
                "src/NovelSpeaker.App/Features/Beta/Resources.xaml",
                "src/NovelSpeaker.App",
                "<ResourceDictionary xmlns:alpha=\"clr-namespace:NovelSpeaker.App.Features.Alpha\" />")
        };

        Assert.Equal(
            [
                "src/NovelSpeaker.App/Features/Alpha/Resources.xaml -> NovelSpeaker.App.Features.Beta",
                "src/NovelSpeaker.App/Features/Beta/Resources.xaml -> NovelSpeaker.App.Features.Alpha"
            ],
            ArchitectureRules.FindFeatureDependencyCycles(files));
    }

    [Fact]
    public void SingletonFeatureRegistrationRuleRejectsAnOrdinaryViewModel()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Features/Books/BooksServiceCollectionExtensions.cs",
                "src/NovelSpeaker.App",
                "services.TryAddSingleton<BooksViewModel>(); services.AddSingleton(typeof(IPage), typeof(BooksPage)); services.AddSingleton(typeof(BooksPage), provider => new BooksPage(provider.GetRequiredService<IStore>())); services.Add(ServiceDescriptor.Singleton<IOther, OtherViewModel>()); services.Add(ServiceDescriptor.Singleton(typeof(IThird), typeof(ThirdViewModel))); namespace NovelSpeaker.App.Features.Books; public sealed class BooksServiceCollectionExtensions;"),
            Source(
                "src/NovelSpeaker.App/Shell/InvalidFeatureRegistration.cs",
                "src/NovelSpeaker.App",
                "using Library = NovelSpeaker.App.Features.Library; namespace NovelSpeaker.App.Shell; public static class InvalidFeatureRegistration { public static void Add(IServiceCollection services) { services.AddSingleton<Library.LibraryViewModel>(); services.AddSingleton(sp => new Library.LibraryViewModel(sp.GetRequiredService<IStore>())); services.AddSingleton<IFeatureView>(sp => new global::NovelSpeaker.App.Features.Library.LibraryViewModel(sp.GetRequiredService<IStore>())); } }")
        };

        Assert.Equal(
            [
                "src/NovelSpeaker.App/Features/Books/BooksServiceCollectionExtensions.cs: BooksPage",
                "src/NovelSpeaker.App/Features/Books/BooksServiceCollectionExtensions.cs: BooksViewModel",
                "src/NovelSpeaker.App/Features/Books/BooksServiceCollectionExtensions.cs: OtherViewModel",
                "src/NovelSpeaker.App/Features/Books/BooksServiceCollectionExtensions.cs: ThirdViewModel",
                "src/NovelSpeaker.App/Shell/InvalidFeatureRegistration.cs: Library.LibraryViewModel",
                "src/NovelSpeaker.App/Shell/InvalidFeatureRegistration.cs: global::NovelSpeaker.App.Features.Library.LibraryViewModel"
            ],
            ArchitectureRules.FindSingletonFeaturePageOrViewModelRegistrations(files));
    }

    [Fact]
    public void SharedFeatureRuleRejectsAFeatureDependency()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Shared/Presentation/InvalidView.cs",
                "src/NovelSpeaker.App",
                "using NovelSpeaker.App.Features.Library; namespace NovelSpeaker.App.Shared.Presentation; public sealed class InvalidView;")
        };

        Assert.Equal(
            ["src/NovelSpeaker.App/Shared/Presentation/InvalidView.cs -> NovelSpeaker.App.Features.Library"],
            ArchitectureRules.FindSharedFeatureDependencies(files));
    }

    [Fact]
    public void SharedFeatureRuleRejectsAFeatureDependencyInXaml()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Shared/Presentation/InvalidView.xaml",
                "src/NovelSpeaker.App",
                "<UserControl xmlns:library=\"clr-namespace:NovelSpeaker.App.Features.Library\" />")
        };

        Assert.Equal(
            ["src/NovelSpeaker.App/Shared/Presentation/InvalidView.xaml -> NovelSpeaker.App.Features.Library"],
            ArchitectureRules.FindSharedFeatureDependencies(files));
    }

    [Fact]
    public void SharedFeatureRuleRejectsAFeatureXamlClass()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Shared/Presentation/InvalidView.xaml",
                "src/NovelSpeaker.App",
                "<UserControl xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"NovelSpeaker.App.Features.Library.InvalidView\" />")
        };

        Assert.Equal(
            ["src/NovelSpeaker.App/Shared/Presentation/InvalidView.xaml -> NovelSpeaker.App.Features.Library.InvalidView"],
            ArchitectureRules.FindSharedFeatureDependencies(files));
    }

    [Fact]
    public void GenericAbstractionRuleRejectsForbiddenDeclarations()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.Application/InvalidEventBus.cs",
                "src/NovelSpeaker.Application",
                "namespace NovelSpeaker.Application; public sealed class InvalidEventBus; public delegate void InvalidMessenger(); public delegate void InvalidGenericEventBus<T>(T value); public interface InvalidServiceLocator;")
        };

        Assert.Equal(
            [
                "src/NovelSpeaker.Application/InvalidEventBus.cs: InvalidEventBus",
                "src/NovelSpeaker.Application/InvalidEventBus.cs: InvalidGenericEventBus",
                "src/NovelSpeaker.Application/InvalidEventBus.cs: InvalidMessenger",
                "src/NovelSpeaker.Application/InvalidEventBus.cs: InvalidServiceLocator"
            ],
            ArchitectureRules.FindForbiddenGenericAbstractionDeclarations(files));
    }

    [Fact]
    public void ReadingProgressWriterRuleRejectsPageDependency()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Features/Books/BooksViewModel.cs",
                "src/NovelSpeaker.App",
                "using NovelSpeaker.Application.Playback; namespace NovelSpeaker.App.Features.Books; public sealed class BooksViewModel(IReadingProgressStore store);"),
            Source(
                "src/NovelSpeaker.App/Shell/MainWindowViewModel.cs",
                "src/NovelSpeaker.App",
                "using NovelSpeaker.Application.Playback; namespace NovelSpeaker.App.Shell; public sealed class MainWindowViewModel(IReadingProgressStore store);")
        };

        Assert.Equal(
            [
                "src/NovelSpeaker.App/Features/Books/BooksViewModel.cs: IReadingProgressStore",
                "src/NovelSpeaker.App/Shell/MainWindowViewModel.cs: IReadingProgressStore"
            ],
            ArchitectureRules.FindReadingProgressWriterDependencies(files));
    }

    [Fact]
    public void LargeListRuleRejectsClearThenAddProjection()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Shared/Presentation/LargeListHelper.cs",
                "src/NovelSpeaker.App",
                """
                namespace NovelSpeaker.App.Shared.Presentation;
                public static class LargeListHelper
                {
                    public static void Replace<T>(ICollection<T> collection, IEnumerable<T> items)
                    {
                        collection.Clear();
                        if (items.Count > 0)
                        {
                            foreach (var item in items.Where(item => item is not null))
                            {
                                if (item is not null)
                                {
                                    collection.Add(item);
                                }
                            }
                        }

                        collection.Clear();
                        foreach (var item in items)
                            if (item is not null)
                                continue;
                            else
                                collection.Add(item);

                        collection.Clear();
                        foreach (var item in items) collection.Add(item);

                        collection.Clear();
                        for (var index = 0; index < 1; index++) collection.Add(items.First());

                        collection.Clear();
                        while (true) { collection.Add(items.First()); break; }
                    }
                }
                """)
        };

        Assert.Equal(
            ["src/NovelSpeaker.App/Shared/Presentation/LargeListHelper.cs"],
            ArchitectureRules.FindLargeListClearThenAddViolations(
                files,
                ["src/NovelSpeaker.App/Shared/Presentation/LargeListHelper.cs"]));
    }

    [Fact]
    public void LargeListRuleAllowsClearThenNonCollectionLoop()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Shared/Presentation/SafeHelper.cs",
                "src/NovelSpeaker.App",
                """
                namespace NovelSpeaker.App.Shared.Presentation;
                public static class SafeHelper
                {
                    public static void Replace<T>(ICollection<T> collection, IEnumerable<T> items, ICollection<T> other)
                    {
                        collection.Clear();
                        foreach (var item in items) other.Add(item);
                    }
                }
                """)
        };

        Assert.Empty(ArchitectureRules.FindLargeListClearThenAddViolations(
            files,
            ["src/NovelSpeaker.App/Shared/Presentation/SafeHelper.cs"]));
    }

    [Fact]
    public void LargeListRuleRejectsAnUnbracedIfElseLoopBody()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Shared/Presentation/UnbracedIfElseHelper.cs",
                "src/NovelSpeaker.App",
                "namespace NovelSpeaker.App.Shared.Presentation; public static class UnbracedIfElseHelper { public static void Replace<T>(ICollection<T> collection, IEnumerable<T> items) { collection.Clear(); foreach (var item in items) if (item is not null) continue; else collection.Add(item); } }")
        };

        Assert.Equal(
            ["src/NovelSpeaker.App/Shared/Presentation/UnbracedIfElseHelper.cs"],
            ArchitectureRules.FindLargeListClearThenAddViolations(
                files,
                ["src/NovelSpeaker.App/Shared/Presentation/UnbracedIfElseHelper.cs"]));
    }

    [Fact]
    public void LargeListRuleRejectsATryCatchLoopBody()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Shared/Presentation/TryCatchHelper.cs",
                "src/NovelSpeaker.App",
                "namespace NovelSpeaker.App.Shared.Presentation; public static class TryCatchHelper { public static void Replace<T>(ICollection<T> collection, IEnumerable<T> items) { collection.Clear(); foreach (var item in items) try { _ = item; } catch { collection.Add(item); } } }")
        };

        Assert.Equal(
            ["src/NovelSpeaker.App/Shared/Presentation/TryCatchHelper.cs"],
            ArchitectureRules.FindLargeListClearThenAddViolations(
                files,
                ["src/NovelSpeaker.App/Shared/Presentation/TryCatchHelper.cs"]));
    }

    [Fact]
    public void PlaybackBoundaryRuleRejectsConcreteCoordinatorDependency()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Features/Playback/InvalidViewModel.cs",
                "src/NovelSpeaker.App",
                "namespace NovelSpeaker.App.Features.Playback; public sealed class InvalidViewModel(PlaybackCoordinator coordinator);")
        };

        Assert.Equal(
            ["src/NovelSpeaker.App/Features/Playback/InvalidViewModel.cs"],
            ArchitectureRules.FindConcretePlaybackCoordinatorDependencies(files, []));
    }

    [Fact]
    public void PlaybackConsumerRuleRequiresReadOnlyContractOrExplicitCommandBoundary()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Features/Playback/ReadOnlyViewModel.cs",
                "src/NovelSpeaker.App",
                "namespace NovelSpeaker.App.Features.Playback; public sealed class ReadOnlyViewModel { public void Apply(PlaybackSnapshot snapshot) { } }"),
            Source(
                "src/NovelSpeaker.App/Features/Playback/PlaybackSnapshotConsumer.cs",
                "src/NovelSpeaker.App",
                "namespace NovelSpeaker.App.Features.Playback; public sealed class PlaybackSnapshotConsumer { public void Apply(PlaybackSnapshot snapshot) { } }"),
            Source(
                "src/NovelSpeaker.App/Features/Playback/UnexpectedCommandViewModel.cs",
                "src/NovelSpeaker.App",
                "namespace NovelSpeaker.App.Features.Playback; public sealed class UnexpectedCommandViewModel(IPlaybackSession session);"),
            Source(
                "src/NovelSpeaker.App/Features/Playback/PlaybackCommandHandler.cs",
                "src/NovelSpeaker.App",
                "namespace NovelSpeaker.App.Features.Playback; public sealed class PlaybackCommandHandler(IPlaybackSession session);")
        };

        Assert.Equal(
            [
                "src/NovelSpeaker.App/Features/Playback/PlaybackCommandHandler.cs",
                "src/NovelSpeaker.App/Features/Playback/PlaybackSnapshotConsumer.cs",
                "src/NovelSpeaker.App/Features/Playback/ReadOnlyViewModel.cs",
                "src/NovelSpeaker.App/Features/Playback/UnexpectedCommandViewModel.cs"
            ],
            ArchitectureRules.FindPlaybackConsumerBoundaryViolations(files, [], []));
    }

    [Fact]
    public void PlaybackOwnerRegistrationRuleFindsAllRegistrationShapes()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.Application/Playback/InvalidRegistration.cs",
                "src/NovelSpeaker.Application",
                "services.AddTransient<IPlaybackSession, PlaybackCoordinator>(); services.Add(ServiceDescriptor.Scoped<IPlaybackSession, PlaybackCoordinator>()); services.AddSingleton(typeof(IPlaybackSession), typeof(PlaybackCoordinator)); services.AddSingleton(typeof(PlaybackCoordinator), provider => new PlaybackCoordinator(provider.GetRequiredService<IStore>())); services.AddSingleton(provider => new PlaybackCoordinator(provider.GetRequiredService<IStore>())); services.AddSingleton<IPlaybackSession>(provider => new PlaybackCoordinator(provider.GetRequiredService<IStore>())); services.AddSingleton<IPlaybackSession>(provider => provider.GetRequiredService<PlaybackCoordinator>());")
        };

        Assert.Equal(
            [
                "src/NovelSpeaker.Application/Playback/InvalidRegistration.cs: Scoped",
                "src/NovelSpeaker.Application/Playback/InvalidRegistration.cs: Singleton",
                "src/NovelSpeaker.Application/Playback/InvalidRegistration.cs: Transient"
            ],
            ArchitectureRules.FindPlaybackCoordinatorRegistrations(files));
    }

    [Fact]
    public void PlaybackOwnerRegistrationRuleRejectsFactoryOnlyRegistration()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.Application/Playback/InvalidFactoryRegistration.cs",
                "src/NovelSpeaker.Application",
                "services.AddSingleton(provider => new global::NovelSpeaker.Application.Playback.PlaybackCoordinator(provider.GetRequiredService<IStore>()));")
        };

        Assert.Equal(
            ["src/NovelSpeaker.Application/Playback/InvalidFactoryRegistration.cs: Singleton"],
            ArchitectureRules.FindPlaybackCoordinatorRegistrations(files));
    }

    [Fact]
    public void FireAndForgetRuleRejectsDirectlyDiscardedAsyncOperation()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Features/InvalidViewModel.cs",
                "src/NovelSpeaker.App",
                """
                namespace NovelSpeaker.App.Features;
                public sealed class InvalidViewModel
                {
                    public void Start() => _ = SaveAsync();
                    private static Task SaveAsync() => Task.CompletedTask;
                }
                """)
        };

        var violations = ArchitectureRules.FindUnregisteredFireAndForgetOperations(files);

        Assert.Equal(
            ["src/NovelSpeaker.App/Features/InvalidViewModel.cs: directly discarded SaveAsync task"],
            violations);
    }

    [Fact]
    public void PublicApiRuleRejectsWpfType()
    {
        var violations = ArchitectureRules.FindForbiddenPublicApiDependencies(
            typeof(ArchitectureRuleContractTests).Assembly,
            typeof(InvalidPublicApiFixture).Namespace!);

        Assert.Contains(
            violations,
            violation => violation.EndsWith(
                $"{nameof(InvalidPublicApiFixture)}.{nameof(InvalidPublicApiFixture.WpfValue)} -> {typeof(DependencyObject).FullName}",
                StringComparison.Ordinal));
    }

    [Fact]
    public void SourceLayoutRuleRejectsNamespaceAndTypeMismatch()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.Domain/Books/Book.cs",
                "src/NovelSpeaker.Domain",
                "namespace NovelSpeaker.Domain.Speech;\npublic sealed class WrongType;")
        };

        var violations = ArchitectureRules.FindSourceLayoutViolations(files);

        Assert.True(
            violations.Count == 2,
            $"Expected two violations but found:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        Assert.Contains(violations, violation => violation.Contains("namespace", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("public types", StringComparison.Ordinal));
    }

    private static SourceFileDescriptor Source(
        string relativePath,
        string projectDirectoryRelativePath,
        string content) =>
        new(relativePath, projectDirectoryRelativePath, content);
}

public sealed class InvalidPublicApiFixture
{
    public DependencyObject? WpfValue { get; init; }
}
