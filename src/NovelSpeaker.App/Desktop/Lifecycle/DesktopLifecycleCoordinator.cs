using System.Threading.Channels;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Desktop.Lifecycle;

internal sealed class DesktopLifecycleCoordinator : IDesktopLifecycleCoordinator, IAsyncDisposable
{
    private readonly object _syncRoot = new();
    private readonly IAppSettingsService _settingsService;
    private readonly IPlaybackSession _playbackSession;
    private readonly IDesktopExitGuard _exitGuard;
    private readonly IProcessShutdownRequest _shutdownRequest;
    private readonly IDesktopLifecyclePlatform _platform;
    private readonly ILogger<DesktopLifecycleCoordinator> _logger;
    private Channel<DesktopLifecycleCommand>? _commands;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _commandProcessor;
    private Task? _stopTask;
    private Task? _exitTask;
    private Task? _exitObservation;
    private bool _started;

    public DesktopLifecycleCoordinator(
        IAppSettingsService settingsService,
        IPlaybackSession playbackSession,
        IDesktopExitGuard exitGuard,
        IProcessShutdownRequest shutdownRequest,
        IDesktopLifecyclePlatform platform,
        ILogger<DesktopLifecycleCoordinator> logger)
    {
        _settingsService = settingsService;
        _playbackSession = playbackSession;
        _exitGuard = exitGuard;
        _shutdownRequest = shutdownRequest;
        _platform = platform;
        _logger = logger;
    }

    public bool IsExitApproved { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Channel<DesktopLifecycleCommand> commands;
        CancellationTokenSource lifetimeCancellation;

        lock (_syncRoot)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            commands = Channel.CreateUnbounded<DesktopLifecycleCommand>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _commands = commands;
            _lifetimeCancellation = lifetimeCancellation;
            _commandProcessor = ProcessCommandsAsync(commands.Reader, lifetimeCancellation.Token);
            _stopTask = null;
            _platform.CommandReceived += OnCommandReceived;
        }

        try
        {
            await _platform.StartAsync(cancellationToken).ConfigureAwait(false);
            if (_settingsService.Current.StartMinimizedToTray)
            {
                await _platform.HideMainWindowAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _platform.ShowMainWindowAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<Task>? platformStopPublication = null;
        Task stopTask;
        lock (_syncRoot)
        {
            if (!_started)
            {
                return _stopTask?.WaitAsync(cancellationToken) ?? Task.CompletedTask;
            }

            _started = false;
            _platform.CommandReceived -= OnCommandReceived;
            _commands!.Writer.TryComplete();
            platformStopPublication = new TaskCompletionSource<Task>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var lifetimeCancellation = _lifetimeCancellation!;
            _stopTask = StopAfterPlatformStopPublishedAsync(
                platformStopPublication.Task,
                _commandProcessor,
                lifetimeCancellation,
                lifetimeCancellation.Token);
            stopTask = _stopTask;
        }

        Task platformStopTask;
        try
        {
            // Start UI-owned tray cleanup immediately after leaving the coordinator
            // lock. App.OnExit may synchronously wait on the shared stop task.
            platformStopTask = _platform.StopAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            platformStopTask = Task.FromException(exception);
        }

        platformStopPublication.SetResult(platformStopTask);
        return stopTask.WaitAsync(cancellationToken);
    }

    public async Task RequestMainWindowCloseAsync(CancellationToken cancellationToken)
    {
        switch (_settingsService.Current.MainWindowCloseBehavior)
        {
            case MainWindowCloseBehavior.MinimizeToTray:
                await _platform.HideMainWindowAsync(cancellationToken).ConfigureAwait(false);
                break;
            case MainWindowCloseBehavior.ExitApplication:
                await RequestExitAsync(cancellationToken).ConfigureAwait(false);
                break;
            case MainWindowCloseBehavior.AskEveryTime:
                var choice = await _platform
                    .PromptForCloseChoiceAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (choice == DesktopCloseChoice.HideToTray)
                {
                    await _platform.HideMainWindowAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (choice == DesktopCloseChoice.ExitApplication)
                {
                    await RequestExitAsync(cancellationToken).ConfigureAwait(false);
                }

                break;
            default:
                throw new InvalidOperationException("不支持的主窗口关闭行为。");
        }
    }

    public Task RequestExitAsync(CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            _exitTask ??= ExitCoreAsync(cancellationToken);
            return _exitTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ExitCoreAsync(CancellationToken cancellationToken)
    {
        // Ensure RequestExitAsync publishes the shared task before a synchronous
        // guard result can request retry cleanup.
        await Task.Yield();
        try
        {
            if (!await _exitGuard.ConfirmExitAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            // Once the user approves exit, page/window lifetime cancellation must not
            // abandon final process cleanup or the final WPF Close.
            await _shutdownRequest.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            IsExitApproved = true;
            await _platform.CloseMainWindowAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            IsExitApproved = false;
            throw;
        }
        finally
        {
            if (!IsExitApproved)
            {
                lock (_syncRoot)
                {
                    _exitTask = null;
                }
            }
        }
    }

    private async Task ProcessCommandsAsync(
        ChannelReader<DesktopLifecycleCommand> reader,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var command in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await ExecuteCommandAsync(command, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        "Desktop lifecycle command {Command} failed with {FailureType}.",
                        command,
                        exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ExecuteCommandAsync(
        DesktopLifecycleCommand command,
        CancellationToken cancellationToken)
    {
        switch (command)
        {
            case DesktopLifecycleCommand.ShowMainWindow:
                await _platform.ShowMainWindowAsync(cancellationToken).ConfigureAwait(false);
                break;
            case DesktopLifecycleCommand.TogglePlayback:
                if (_playbackSession.CurrentSnapshot.State == PlaybackState.Playing)
                {
                    await _playbackSession.PauseAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (_playbackSession.CurrentSnapshot.State == PlaybackState.Paused)
                {
                    await _playbackSession.ResumeAsync(cancellationToken).ConfigureAwait(false);
                }

                break;
            case DesktopLifecycleCommand.OpenMiniPlayer:
                // MINI-503 owns the mini-player window. The tray item remains disabled
                // until that slice supplies the single command target.
                break;
            case DesktopLifecycleCommand.ExitApplication:
                var exitTask = RequestExitAsync(cancellationToken);
                lock (_syncRoot)
                {
                    _exitObservation = ObserveTrayExitAsync(exitTask);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private async Task StopAfterPlatformStopPublishedAsync(
        Task<Task> platformStopPublication,
        Task? commandProcessor,
        CancellationTokenSource lifetimeCancellation,
        CancellationToken lifetimeToken)
    {
        var platformStopTask = await platformStopPublication.ConfigureAwait(false);
        List<Exception>? failures = null;

        try
        {
            lifetimeCancellation.Cancel();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            if (commandProcessor is not null)
            {
                await commandProcessor.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            await platformStopTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
        finally
        {
            lifetimeCancellation.Dispose();
            lock (_syncRoot)
            {
                _commands = null;
                _lifetimeCancellation = null;
                _commandProcessor = null;
            }
        }

        if (failures is [var single])
        {
            ExceptionDispatchInfo.Capture(single).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(failures);
        }
    }

    private void OnCommandReceived(object? sender, DesktopLifecycleCommand command)
    {
        lock (_syncRoot)
        {
            _commands?.Writer.TryWrite(command);
        }
    }

    private async Task ObserveTrayExitAsync(Task exitTask)
    {
        try
        {
            await exitTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Tray exit command failed with {FailureType}.",
                exception.GetType().Name);
        }
    }
}
