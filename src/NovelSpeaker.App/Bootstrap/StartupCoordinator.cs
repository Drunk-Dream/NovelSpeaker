using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Bootstrap;

/// <summary>
/// Owns the process startup cancellation source and serial startup stage progression.
/// </summary>
internal sealed class StartupCoordinator : IAsyncDisposable
{
    private readonly IStartupRuntime _runtime;
    private readonly CancellationTokenSource _processCancellation = new();
    private CancellationTokenSource? _startupCancellation;
    private readonly object _shutdownGate = new();
    private Task? _shutdownTask;
    private int _started;
    private int _disposed;

    public StartupCoordinator(IStartupRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public CancellationToken ProcessToken => _processCancellation.Token;

    public async Task<StartupResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("启动协调器只能运行一次。");
        }

        _startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _processCancellation.Token,
            cancellationToken);
        var token = _startupCancellation.Token;

        try
        {
            try
            {
                _runtime.ShowStartupStatus();
            }
            catch (Exception exception)
            {
                throw new StartupStageException(StartupStage.Directories, exception);
            }

            await RunRequiredStageAsync(
                StartupStage.Directories,
                _runtime.PrepareDirectoriesAsync,
                token).ConfigureAwait(true);

            var settings = await RunSettingsStageAsync(token).ConfigureAwait(true);

            await RunRequiredStageAsync(
                StartupStage.Logging,
                stageToken => _runtime.InitializeLoggingAsync(settings, stageToken),
                token).ConfigureAwait(true);

            await RunRequiredStageAsync(
                StartupStage.DependencyInjection,
                stageToken => _runtime.BuildServicesAsync(settings, stageToken),
                token).ConfigureAwait(true);

            await RunRequiredStageAsync(
                StartupStage.Database,
                _runtime.InitializeDatabaseAsync,
                token).ConfigureAwait(true);

            await RunThemeStageAsync(token).ConfigureAwait(true);

            await RunRequiredStageAsync(
                StartupStage.Shell,
                _runtime.ShowShellAsync,
                token).ConfigureAwait(true);

            return StartupResult.Successful;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _runtime.CloseStartupStatus();
            return StartupResult.Cancelled;
        }
        catch (StartupStageException exception)
        {
            var failure = StartupFailureProjector.Project(exception.Stage);
            TryRecordFailure(exception.Stage, failure.Message, exception.InnerException!);
            _runtime.ShowStartupFailure(failure);
            _runtime.CloseStartupStatus();
            return StartupResult.Failed(failure);
        }
    }

    public void Cancel() => _processCancellation.Cancel();

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_shutdownGate)
        {
            _shutdownTask ??= ShutdownCoreAsync(cancellationToken);
            return _shutdownTask;
        }
    }

    public void RecordUnhandledFailure(string stage, string safeMessage, Exception? exception)
    {
        TryRecordFailure(
            StartupStage.Shell,
            $"{stage}: {safeMessage}",
            exception ?? new InvalidOperationException("未提供异常对象。"));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await ShutdownAsync().ConfigureAwait(false);
        }
        finally
        {
            _startupCancellation?.Dispose();
            _processCancellation.Dispose();
        }
    }

    private async Task ShutdownCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            _runtime.BeginShutdown();
        }
        catch (Exception exception)
        {
            TryRecordLifecycleFailure(
                "shutdown-gate",
                "阻止新操作失败，将继续关闭。",
                exception);
        }

        await RunShutdownStepAsync(
            "desktop-lifecycle-shutdown",
            "注销系统托盘失败，将继续关闭。",
            _runtime.StopDesktopLifecycleAsync,
            cancellationToken).ConfigureAwait(false);

        await RunShutdownStepAsync(
            "media-control-shutdown",
            "注销系统媒体控制失败，将继续关闭。",
            _runtime.StopMediaControlsAsync,
            cancellationToken).ConfigureAwait(false);

        await RunShutdownStepAsync(
            "playback-shutdown",
            "保存并结束播放失败，将继续关闭。",
            _runtime.StopPlaybackAsync,
            cancellationToken).ConfigureAwait(false);

        _processCancellation.Cancel();

        await RunShutdownStepAsync(
            "background-shutdown",
            "等待后台任务退出失败，将继续关闭。",
            _runtime.WaitForBackgroundTasksAsync,
            cancellationToken).ConfigureAwait(false);

        await RunShutdownStepAsync(
            "flush-shutdown",
            "刷新设置或日志失败，将继续关闭。",
            _runtime.FlushAsync,
            cancellationToken).ConfigureAwait(false);

        try
        {
            await _runtime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TryRecordLifecycleFailure(
                "resource-disposal",
                "释放应用资源失败，将继续关闭。",
                exception);
        }
    }

    private async Task RunShutdownStepAsync(
        string name,
        string safeMessage,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TryRecordLifecycleFailure(name, safeMessage, exception);
        }
    }

    private async Task<AppSettings> RunSettingsStageAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _runtime.ReportStageAsync(StartupStage.Settings, cancellationToken).ConfigureAwait(true);
            return await _runtime.LoadSettingsAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new StartupStageException(StartupStage.Settings, exception);
        }
    }

    private async Task RunRequiredStageAsync(
        StartupStage stage,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await _runtime.ReportStageAsync(stage, cancellationToken).ConfigureAwait(true);
            await action(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new StartupStageException(stage, exception);
        }
    }

    private async Task RunThemeStageAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _runtime.ReportStageAsync(StartupStage.Theme, cancellationToken).ConfigureAwait(true);
            await _runtime.ApplyThemeAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            try
            {
                await _runtime.ApplyFallbackThemeAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception fallbackException)
            {
                TryRecordFailure(
                    StartupStage.Theme,
                    "安全默认主题也无法应用，将继续使用当前窗口资源。",
                    fallbackException);
            }

            TryRecordFailure(
                StartupStage.Theme,
                "界面主题应用失败，已使用安全默认外观继续启动。",
                exception);
        }
    }

    private void TryRecordFailure(StartupStage stage, string safeMessage, Exception exception)
    {
        try
        {
            _runtime.RecordFailure(stage, safeMessage, exception);
        }
        catch
        {
            // Startup diagnostics are best effort and must not replace the original safe result.
        }
    }

    private void TryRecordLifecycleFailure(string name, string safeMessage, Exception exception)
    {
        try
        {
            _runtime.RecordLifecycleFailure(name, safeMessage, exception);
        }
        catch
        {
            // Shutdown diagnostics are best effort and must not block resource release.
        }
    }

    private sealed class StartupStageException : Exception
    {
        public StartupStageException(StartupStage stage, Exception innerException)
            : base("启动阶段失败。", innerException)
        {
            Stage = stage;
        }

        public StartupStage Stage { get; }
    }
}
