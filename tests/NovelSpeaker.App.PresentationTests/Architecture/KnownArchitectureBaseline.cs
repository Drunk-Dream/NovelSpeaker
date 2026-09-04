namespace NovelSpeaker.App.PresentationTests.Architecture;

internal static class KnownArchitectureBaseline
{
    public static readonly IReadOnlySet<string> AppInfrastructureSourceFiles =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "src/NovelSpeaker.App/Bootstrap/WpfStartupRuntime.cs"
        };

    public static readonly IReadOnlySet<string> SourceLayoutViolations =
        new HashSet<string>(StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> ViewModelForbiddenPublicApiDependencies =
        new HashSet<string>(StringComparer.Ordinal);

    // T003 moves this reusable book presentation primitive into Features/Books/Shared.
    public static readonly IReadOnlySet<string> SharedFeatureSourceDependencies =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "src/NovelSpeaker.App/Shared/Presentation/Books/BookCoverView.xaml.cs -> NovelSpeaker.App.Features.Library"
        };

    // T003 removes these direct Library <-> BookDetails dependencies.
    public static readonly IReadOnlySet<string> FeatureDependencyCycles =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "src/NovelSpeaker.App/Features/BookDetails/BookDetailsViewModel.cs -> NovelSpeaker.App.Features.Library",
            "src/NovelSpeaker.App/Features/Library/LibraryViewModel.cs -> NovelSpeaker.App.Features.BookDetails"
        };

    // T002 converted ordinary Feature ViewModels to transient registrations.
    public static readonly IReadOnlySet<string> FeaturePageOrViewModelSingletonRegistrations =
        new HashSet<string>(StringComparer.Ordinal);

    // T005 replaces this helper with a batch catalog/projection primitive.
    public static readonly IReadOnlySet<string> LargeListClearThenAddViolations =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "src/NovelSpeaker.App/Shared/Presentation/ViewModelCollectionExtensions.cs"
        };
}
