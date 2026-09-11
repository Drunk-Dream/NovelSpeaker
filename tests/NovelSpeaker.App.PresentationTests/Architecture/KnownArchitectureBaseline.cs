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

    // Temporary Application module debts are keyed by the exact source file, edge,
    // target namespace and type. Values identify the task that must remove each debt.
    public static readonly IReadOnlyDictionary<string, string> ApplicationModuleDependencyDebts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // T002: Books still registers the legacy Playback.Cache speech-plan service.
            [Dependency(
                "src/NovelSpeaker.Application/Books/BooksRegistration.cs",
                ApplicationModule.Books,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "ChapterSpeechPlanService")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Books/BooksRegistration.cs",
                ApplicationModule.Books,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "IChapterSpeechPlanService")] = "T002",

            // T003: Books still publishes Cache-specific invalidation directly.
            [Dependency(
                "src/NovelSpeaker.Application/Books/TextProcessing/RegexReplacementRuleWorkspaceService.cs",
                ApplicationModule.Books,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "CacheInvalidation")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Books/TextProcessing/RegexReplacementRuleWorkspaceService.cs",
                ApplicationModule.Books,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "CacheInvalidationAspect")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Books/TextProcessing/RegexReplacementRuleWorkspaceService.cs",
                ApplicationModule.Books,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "ICacheInvalidationCoordinator")] = "T003",

            // T002/T003: Settings still exposes a Cache-owned limit port and invalidates Cache.
            [Dependency(
                "src/NovelSpeaker.Application/Settings/AppSettingsService.cs",
                ApplicationModule.Settings,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "IAudioCacheLimitProvider")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Settings/AppSettingsService.cs",
                ApplicationModule.Settings,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "CacheInvalidation")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Settings/AppSettingsService.cs",
                ApplicationModule.Settings,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "CacheInvalidationAspect")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Settings/AppSettingsService.cs",
                ApplicationModule.Settings,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "ICacheInvalidationCoordinator")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Settings/SettingsRegistration.cs",
                ApplicationModule.Settings,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "IAudioCacheLimitProvider")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Settings/SettingsRegistration.cs",
                ApplicationModule.Settings,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "ICacheInvalidationCoordinator")] = "T003",

            // T002: Speech still uses the legacy Cache identity writer.
            [Dependency(
                "src/NovelSpeaker.Application/Speech/Compilation/TtsRuleFingerprint.cs",
                ApplicationModule.Speech,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "CanonicalIdentityWriter")] = "T002",

            // T003: Speech still publishes Cache-specific invalidation directly.
            [Dependency(
                "src/NovelSpeaker.Application/Speech/Rules/TtsRuleEditorUseCase.cs",
                ApplicationModule.Speech,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "CacheInvalidation")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Speech/Rules/TtsRuleEditorUseCase.cs",
                ApplicationModule.Speech,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "CacheInvalidationAspect")] = "T003",
            [Dependency(
                "src/NovelSpeaker.Application/Speech/Rules/TtsRuleEditorUseCase.cs",
                ApplicationModule.Speech,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Playback.Cache",
                "ICacheInvalidationCoordinator")] = "T003",

            // T002: Settings currently implements a Books-owned query port.
            [Dependency(
                "src/NovelSpeaker.Application/Settings/AppSettingsService.cs",
                ApplicationModule.Settings,
                ApplicationModule.Books,
                "NovelSpeaker.Application.Books",
                "ITextSegmentationOptionsProvider")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Settings/SettingsRegistration.cs",
                ApplicationModule.Settings,
                ApplicationModule.Books,
                "NovelSpeaker.Application.Books",
                "ITextSegmentationOptionsProvider")] = "T002",

            // T002: Speech test execution still consumes the legacy Playback audio role.
            [Dependency(
                "src/NovelSpeaker.Application/Speech/Testing/TtsRuleTestService.cs",
                ApplicationModule.Speech,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback.Audio",
                "PlaybackErrorMapper")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Speech/Testing/TtsRuleTestService.cs",
                ApplicationModule.Speech,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "IAudioPlayer")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Speech/Testing/TtsRuleTestService.cs",
                ApplicationModule.Speech,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "IAudioPlayerFactory")] = "T002",

            // T002: Speech still consumes the legacy Cache identity value from the reverse edge.
            [Dependency(
                "src/NovelSpeaker.Application/Speech/Compilation/TtsRuleFingerprint.cs",
                ApplicationModule.Speech,
                ApplicationModule.Cache,
                "NovelSpeaker.Application.Cache",
                "Fingerprint")] = "T002",

            // T002: Legacy Cache services still depend on Playback-owned role/query types.
            [Dependency(
                "src/NovelSpeaker.Application/Playback/Cache/CacheCatalog.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "IBookPlaybackMetadataQuery")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/Cache/CacheCoverageQuery.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "IBookPlaybackMetadataQuery")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/Cache/CacheCoverageQuery.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "ISelectedTtsRuleProvider")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/Cache/CacheCoverageQuery.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "PlaybackChapterMetadata")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/Cache/CacheCoverageQuery.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "SelectedPlaybackRule")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/Cache/ICacheCoverageQuery.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "PlaybackChapterMetadata")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/Cache/SpeechPlanRepairCoordinator.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "IBookPlaybackContentService")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/Cache/SpeechPlanRepairRequestor.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "IBookPlaybackMetadataQuery")] = "T002",

            // T002: Nested legacy Cache namespaces implicitly consume Playback types.
            [Dependency(
                "src/NovelSpeaker.Application/Playback/ActiveCache/ActiveCacheCoordinator.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "IBookPlaybackContentService")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/ActiveCache/ActiveCacheCoordinator.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "IPlaybackAudioProvider")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/ActiveCache/ActiveCacheCoordinator.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "ISelectedTtsRuleProvider")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/ActiveCache/ActiveCacheCoordinator.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "PlaybackAudioPriority")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/ActiveCache/ActiveCacheCoordinator.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "PlaybackAudioRequest")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/ActiveCache/ActiveCacheCoordinator.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "PlaybackAudioResult")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/ActiveCache/ActiveCacheCoordinator.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "SelectedPlaybackRule")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/Export/ExportChaptersService.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "IBookPlaybackMetadataQuery")] = "T002",
            [Dependency(
                "src/NovelSpeaker.Application/Playback/Export/ExportChaptersService.cs",
                ApplicationModule.Cache,
                ApplicationModule.Playback,
                "NovelSpeaker.Application.Playback",
                "ISelectedTtsRuleProvider")] = "T002",
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
