using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Application.Diagnostics;

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

    void BeginShutdown();

    Task StopDesktopLifecycleAsync(CancellationToken cancellationToken);

    Task StopMediaControlsAsync(CancellationToken cancellationToken);

    Task StopPlaybackAsync(CancellationToken cancellationToken);

    Task WaitForBackgroundTasksAsync(CancellationToken cancellationToken);

    Task FlushAsync(CancellationToken cancellationToken);

    Task NotifyProcessExitAsync(ProcessExitReason exitReason, CancellationToken cancellationToken);

    void RecordProcessFailure(ProcessFailure failure, string source, string safeMessage, Exception? exception);

    void RecordFailure(StartupStage stage, string safeMessage, Exception exception);

    void RecordLifecycleFailure(string name, string safeMessage, Exception? exception);

    void ShowStartupFailure(StartupFailure failure);

    void CloseStartupStatus();
}
