using System.Windows;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class ArchitectureRuleContractTests
{
    [Fact]
    public void ApplicationModuleRuleDetectsAForbiddenBooksToCacheDependency()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.Application/Books/BooksService.cs",
                "src/NovelSpeaker.Application",
                "using NovelSpeaker.Application.Cache; namespace NovelSpeaker.Application.Books; public sealed class BooksService(IChapterSpeechPlanService service);"),
            Source(
                "src/NovelSpeaker.Application/Cache/IChapterSpeechPlanService.cs",
                "src/NovelSpeaker.Application",
                "namespace NovelSpeaker.Application.Cache; public interface IChapterSpeechPlanService;")
        };

        var dependencies = ArchitectureRules.FindApplicationModuleDependencies(files)
            .Select(dependency => dependency.Display)
            .ToArray();

        Assert.Equal(
            [
                "src/NovelSpeaker.Application/Books/BooksService.cs: Books -> Cache (NovelSpeaker.Application.Cache.IChapterSpeechPlanService)"
            ],
            dependencies);

        var violations = ArchitectureRules.FindApplicationModuleDependencyViolations(files);

        Assert.Equal(
            [
                "src/NovelSpeaker.Application/Books/BooksService.cs: Books -> Cache (NovelSpeaker.Application.Cache.IChapterSpeechPlanService)"
            ],
            violations);
    }

    [Fact]
    public void ApplicationModuleRuleFindsAllApplicationModuleCycles()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.Application/Books/BooksService.cs",
                "src/NovelSpeaker.Application",
                "using NovelSpeaker.Application.Cache; namespace NovelSpeaker.Application.Books; public sealed class BooksService(ICacheCatalog catalog, ICacheCatalogV2 secondCatalog);"),
            Source(
                "src/NovelSpeaker.Application/Cache/ICacheCatalog.cs",
                "src/NovelSpeaker.Application",
                "using NovelSpeaker.Application.Books; namespace NovelSpeaker.Application.Cache; public interface ICacheCatalog(IBookLibraryQuery query); public interface ICacheCatalogV2;"),
            Source(
                "src/NovelSpeaker.Application/Books/IBookLibraryQuery.cs",
                "src/NovelSpeaker.Application",
                "namespace NovelSpeaker.Application.Books; public interface IBookLibraryQuery;")
        };

        Assert.Equal(
            [
                "src/NovelSpeaker.Application/Books/BooksService.cs: Books -> Cache (NovelSpeaker.Application.Cache.ICacheCatalog)",
                "src/NovelSpeaker.Application/Books/BooksService.cs: Books -> Cache (NovelSpeaker.Application.Cache.ICacheCatalogV2)",
                "src/NovelSpeaker.Application/Cache/ICacheCatalog.cs: Cache -> Books (NovelSpeaker.Application.Books.IBookLibraryQuery)"
            ],
            ArchitectureRules.FindApplicationModuleDependencyCycles(files));
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
