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
        "A page reaches its first useful interactive state.");

    public static OperationDefinition UiDispatcherStall { get; } = Create(
        "ui.dispatcher-stall",
        "UI dispatcher work",
        "A UI dispatcher callback waits or runs before returning to the message loop.");

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

    public static OperationDefinition CacheOperation { get; } = Create(
        "cache.operation",
        "Cache operation",
        "A stable cache operation is executed.");

    public static OperationDefinition StorageQuery { get; } = Create(
        "storage.query",
        "Storage query",
        "A stable application storage query is executed.");

    public static IReadOnlyList<OperationDefinition> All { get; } = Array.AsReadOnly(
    [
        AppStartup,
        AppShutdown,
        UiNavigation,
        UiPageCriticalLoad,
        UiDispatcherStall,
        PlaybackStart,
        PlaybackChapterSwitch,
        TtsRequest,
        TtsRetry,
        CacheOperation,
        StorageQuery
    ]);

    private static OperationDefinition Create(string id, string name, string description) =>
        new(new OperationId(id), name, description);
}
