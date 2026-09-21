namespace NovelSpeaker.Application.Observability;

/// <summary>
/// The stable operation vocabulary shared by observability consumers.
/// </summary>
public static class OperationCatalog
{
    public static OperationDefinition AppStartup { get; } = Create(
        "app.startup",
        "Application startup",
        "The application process initializes its user-facing services.");

    public static OperationDefinition AppShutdown { get; } = Create(
        "app.shutdown",
        "Application shutdown",
        "The application process shuts down its user-facing services.");

    public static OperationDefinition UiNavigation { get; } = Create(
        "ui.navigation",
        "UI navigation",
        "The user navigates between application surfaces.");

    public static OperationDefinition UiPageCriticalLoad { get; } = Create(
        "ui.page-critical-load",
        "Critical page load",
        "A page reaches its first useful interactive state when no stable surface-specific definition applies.");

    public static OperationDefinition UiLibraryLoad { get; } = Create(
        "ui.library-load", "Library load", "The Library surface reaches its first useful interactive state.");

    public static OperationDefinition UiBookDetailsLoad { get; } = Create(
        "ui.book-details-load", "Book details load", "The Book Details surface reaches its first useful interactive state.");

    public static OperationDefinition UiPlayerLoad { get; } = Create(
        "ui.player-load", "Player load", "The Player surface reaches its first useful interactive state.");

    public static OperationDefinition UiSettingsLoad { get; } = Create(
        "ui.settings-load", "Settings load", "The Settings surface reaches its first useful interactive state.");

    public static OperationDefinition UiCacheLoad { get; } = Create(
        "ui.cache-load", "Cache load", "The Cache surface reaches its first useful interactive state.");

    public static OperationDefinition UiRulesLoad { get; } = Create(
        "ui.rules-load", "Rules load", "The Rules surface reaches its first useful interactive state.");

    public static OperationDefinition UiDispatcherStall { get; } = Create(
        "ui.dispatcher-stall",
        "UI dispatcher stall",
        "An explicitly detected dispatcher wait that exceeds the observer's abnormal-wait threshold; ordinary dispatch is excluded.");

    public static OperationDefinition PlaybackStart { get; } = Create(
        "playback.start",
        "Playback start",
        "Playback begins for the selected reading session.");

    public static OperationDefinition PlaybackChapterSwitch { get; } = Create(
        "playback.chapter-switch",
        "Playback chapter switch",
        "Playback moves to another chapter.");

    public static OperationDefinition TtsRequest { get; } = Create(
        "tts.request",
        "TTS request",
        "The application submits one text-to-speech request.");

    public static OperationDefinition TtsRetry { get; } = Create(
        "tts.retry",
        "TTS retry",
        "The application retries a text-to-speech request.");

    public static OperationDefinition CacheAudioGeneration { get; } = Create(
        "cache.audio-generation",
        "Cache audio generation",
        "Audio is generated for a cache request.");

    public static OperationDefinition CacheCompletenessCheck { get; } = Create(
        "cache.completeness-check",
        "Cache completeness check",
        "Cache completeness is queried or repaired.");

    public static OperationDefinition StorageConnectionOpen { get; } = Create(
        "storage.connection-open",
        "Storage connection open",
        "An application SQLite connection is opened and initialized.");

    public static OperationDefinition DiagnosticsAction { get; } = Create(
        "diagnostics.action",
        "Diagnostics action",
        "A user-visible diagnostic operation is executed.");

    public static IReadOnlyList<OperationDefinition> All { get; } = Array.AsReadOnly(
    [
        AppStartup,
        AppShutdown,
        UiNavigation,
        UiPageCriticalLoad,
        UiLibraryLoad,
        UiBookDetailsLoad,
        UiPlayerLoad,
        UiSettingsLoad,
        UiCacheLoad,
        UiRulesLoad,
        UiDispatcherStall,
        PlaybackStart,
        PlaybackChapterSwitch,
        TtsRequest,
        TtsRetry,
        CacheAudioGeneration,
        CacheCompletenessCheck,
        StorageConnectionOpen,
        DiagnosticsAction
    ]);

    private static OperationDefinition Create(string id, string name, string description) =>
        new(new OperationId(id), name, description);
}
