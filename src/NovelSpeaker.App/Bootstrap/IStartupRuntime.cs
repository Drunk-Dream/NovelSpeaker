using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Bootstrap;

/// <summary>
/// Isolates the WPF and composition-root operations driven by the startup state machine.
/// </summary>
internal interface IStartupRuntime : IAsyncDisposable
{
    void ShowStartupStatus();

    Task ReportStageAsync(StartupStage stage, CancellationToken cancellationToken);

    Task PrepareDirectoriesAsync(CancellationToken cancellationToken);

    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken);

    Task InitializeLoggingAsync(AppSettings settings, CancellationToken cancellationToken);

    Task BuildServicesAsync(AppSettings settings, CancellationToken cancellationToken);

    Task InitializeDatabaseAsync(CancellationToken cancellationToken);

    Task ApplyThemeAsync(CancellationToken cancellationToken);

    Task ApplyFallbackThemeAsync(CancellationToken cancellationToken);

    Task ShowShellAsync(CancellationToken cancellationToken);

    void RecordFailure(StartupStage stage, string safeMessage, Exception exception);

    void ShowStartupFailure(StartupFailure failure);

    void CloseStartupStatus();
}
