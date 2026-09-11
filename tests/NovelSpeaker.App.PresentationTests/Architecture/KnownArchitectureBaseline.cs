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

    public static readonly IReadOnlySet<string> SharedFeatureSourceDependencies =
        new HashSet<string>(StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> FeatureDependencyCycles =
        new HashSet<string>(StringComparer.Ordinal);

    // T002 converted ordinary Feature ViewModels to transient registrations.
    public static readonly IReadOnlySet<string> FeaturePageOrViewModelSingletonRegistrations =
        new HashSet<string>(StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> LargeListClearThenAddViolations =
        new HashSet<string>(StringComparer.Ordinal);

    // T003 temporarily keeps the source modules independent of Cache-owned invalidation details.
    public static readonly IReadOnlyDictionary<string, string> ApplicationModuleDependencyDebts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Dependency(
                "src/NovelSpeaker.Application/Books/TextProcessing/RegexReplacementRuleWorkspaceService.cs",
                ApplicationModule.Books,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "CacheInvalidation")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Books/TextProcessing/RegexReplacementRuleWorkspaceService.cs",
                ApplicationModule.Books,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "CacheInvalidationAspect")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Books/TextProcessing/RegexReplacementRuleWorkspaceService.cs",
                ApplicationModule.Books,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "ICacheInvalidationCoordinator")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Settings/AppSettingsService.cs",
                ApplicationModule.Settings,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "CacheInvalidation")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Settings/AppSettingsService.cs",
                ApplicationModule.Settings,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "CacheInvalidationAspect")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Settings/AppSettingsService.cs",
                ApplicationModule.Settings,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "ICacheInvalidationCoordinator")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Settings/SettingsRegistration.cs",
                ApplicationModule.Settings,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "ICacheInvalidationCoordinator")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Speech/Rules/TtsRuleEditorUseCase.cs",
                ApplicationModule.Speech,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "CacheInvalidation")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Speech/Rules/TtsRuleEditorUseCase.cs",
                ApplicationModule.Speech,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "CacheInvalidationAspect")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Speech/Rules/TtsRuleEditorUseCase.cs",
                ApplicationModule.Speech,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "ICacheInvalidationCoordinator")] = "T003",
        };

    private static string Dependency(
        string sourcePath,
        ApplicationModule sourceModule,
        ApplicationModule targetModule,
        string targetNamespace,
        string targetType) =>
        ApplicationModuleDependency.CreateIdentity(
            sourcePath,
            sourceModule,
            targetModule,
            targetNamespace,
            targetType);
}
