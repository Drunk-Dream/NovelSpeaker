using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Observability;

namespace NovelSpeaker.Infrastructure.Diagnostics;

/// <summary>
/// The stable production-log event vocabulary.
/// </summary>
public static class LogEventRegistry
{
    public static LogEventDefinition Unclassified { get; } = Define(
        1000,
        "event.unclassified",
        "diagnostics",
        null,
        LogLevel.Information,
        "A caller supplied no registered event definition.");

    public static LogEventDefinition StartupStage { get; } = Define(
        1001,
        "app.startup.stage",
        "startup",
        OperationCatalog.AppStartup,
        LogLevel.Information,
        "A startup stage reported progress.");

    public static LogEventDefinition StartupFailure { get; } = Define(
        1002,
        "app.startup.failure",
        "startup",
        OperationCatalog.AppStartup,
        LogLevel.Error,
        "A startup stage failed.");

    public static LogEventDefinition LifecycleFailure { get; } = Define(
        1003,
        "app.lifecycle.failure",
        "lifecycle",
        OperationCatalog.AppShutdown,
        LogLevel.Error,
        "A process lifecycle operation failed or degraded.");

    public static LogEventDefinition TtsRequestFailed { get; } = Define(
        1101,
        "tts.request.failed",
        "speech",
        OperationCatalog.TtsRequest,
        LogLevel.Error,
        "A text-to-speech request failed.");

    public static LogEventDefinition TtsResponseValidationFailed { get; } = Define(
        1102,
        "tts.response-validation.failed",
        "speech",
        OperationCatalog.TtsRequest,
        LogLevel.Error,
        "A text-to-speech response could not be validated.");

    public static LogEventDefinition TtsCompilationFailed { get; } = Define(
        1103,
        "tts.compilation.failed",
        "speech",
        OperationCatalog.TtsRequest,
        LogLevel.Error,
        "A text-to-speech rule could not be compiled.");

    public static LogEventDefinition TtsRuleTestFailed { get; } = Define(
        1104,
        "tts.rule-test.failed",
        "speech",
        OperationCatalog.TtsRequest,
        LogLevel.Error,
        "A text-to-speech rule test failed.");

    public static LogEventDefinition CacheOperationFailed { get; } = Define(
        1201,
        "cache.operation.failed",
        "cache",
        OperationCatalog.CacheAudioGeneration,
        LogLevel.Error,
        "A cache operation failed.");

    public static LogEventDefinition CacheCompletenessUnavailable { get; } = Define(
        1202,
        "cache.completeness.unavailable",
        "cache",
        OperationCatalog.CacheCompletenessCheck,
        LogLevel.Warning,
        "Cache completeness information was unavailable.");

    public static LogEventDefinition PlaybackContentUnavailable { get; } = Define(
        1301,
        "playback.content.unavailable",
        "playback",
        OperationCatalog.PlaybackStart,
        LogLevel.Warning,
        "Playback content was unavailable.");

    public static LogEventDefinition UiNavigationFailure { get; } = Define(
        1401,
        "ui.navigation.failure",
        "ui",
        OperationCatalog.UiNavigation,
        LogLevel.Error,
        "A UI navigation or projection operation failed.");

    public static LogEventDefinition UiAppearanceFallback { get; } = Define(
        1402,
        "ui.appearance.fallback",
        "ui",
        OperationCatalog.UiPageCriticalLoad,
        LogLevel.Warning,
        "The UI appearance adapter fell back to default behavior.");

    public static LogEventDefinition DiagnosticsOperationFailed { get; } = Define(
        1501,
        "diagnostics.operation.failed",
        "diagnostics",
        OperationCatalog.DiagnosticsAction,
        LogLevel.Error,
        "A user-visible diagnostic operation failed.");

    public static IReadOnlyList<LogEventDefinition> All { get; } = Array.AsReadOnly(
    [
        Unclassified,
        StartupStage,
        StartupFailure,
        LifecycleFailure,
        TtsRequestFailed,
        TtsResponseValidationFailed,
        TtsCompilationFailed,
        TtsRuleTestFailed,
        CacheOperationFailed,
        CacheCompletenessUnavailable,
        PlaybackContentUnavailable,
        UiNavigationFailure,
        UiAppearanceFallback,
        DiagnosticsOperationFailed
    ]);

    private static readonly IReadOnlyDictionary<int, LogEventDefinition> ById =
        All.ToDictionary(definition => definition.Id);

    private static readonly IReadOnlyDictionary<string, LogEventDefinition> ByCategory =
        new Dictionary<string, LogEventDefinition>(StringComparer.Ordinal)
        {
            ["MainWindowAppearanceConfigurator"] = UiAppearanceFallback,
            ["MediaControlFailureReporter"] = UiNavigationFailure,
            ["DesktopLifecycleCoordinator"] = LifecycleFailure,
            ["MiniPlayerViewModel"] = UiNavigationFailure
        };

    public static LogEventDefinition Resolve(EventId eventId, string categoryName)
    {
        if (eventId.Id != 0 && ById.TryGetValue(eventId.Id, out var definition))
        {
            return definition;
        }

        var shortCategoryName = categoryName[(categoryName.LastIndexOf('.') + 1)..];
        return (ByCategory.TryGetValue(categoryName, out definition) ||
                ByCategory.TryGetValue(shortCategoryName, out definition))
            ? definition
            : Unclassified;
    }

    private static LogEventDefinition Define(
        int id,
        string eventName,
        string category,
        OperationDefinition? operation,
        LogLevel defaultLevel,
        string description) =>
        new(id, eventName, category, operation, defaultLevel, description);
}
