using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Application.Desktop.MediaControls;

internal sealed class MediaControlCoordinator : IMediaControlCoordinator, IAsyncDisposable
{
    private readonly IMediaControlPlatform _platform;
    private readonly IPlaybackSession _playbackSession;
    private readonly IMediaControlFailureReporter _failureReporter;
    private readonly object _gate = new();
    private Channel<WorkItem>? _workItems;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _worker;
    private Task? _stopTask;
    private MediaControlMetadata? _lastQueuedMetadata;
    private bool _started;

    public MediaControlCoordinator(
        IMediaControlPlatform platform,
        IPlaybackSession playbackSession,
        IMediaControlFailureReporter failureReporter)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _playbackSession = playbackSession ?? throw new ArgumentNullException(nameof(playbackSession));
        _failureReporter = failureReporter ?? throw new ArgumentNullException(nameof(failureReporter));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Channel<WorkItem> workItems;
        CancellationTokenSource lifetimeCancellation;

        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            workItems = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _workItems = workItems;
            _lifetimeCancellation = lifetimeCancellation;
            _stopTask = null;
            _lastQueuedMetadata = null;
            _worker = RunAsync(workItems.Reader, lifetimeCancellation.Token);
            _platform.CommandReceived += OnCommandReceived;
            _playbackSession.SnapshotChanged += OnSnapshotChanged;
        }

        try
        {
            await _platform.StartAsync(cancellationToken).ConfigureAwait(false);
            EnqueueMetadata(_playbackSession.CurrentSnapshot);
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource? startSignal = null;
        Task stopTask;

        lock (_gate)
        {
            if (!_started)
            {
                return _stopTask?.WaitAsync(cancellationToken) ?? Task.CompletedTask;
            }

            _started = false;
            _platform.CommandReceived -= OnCommandReceived;
            _playbackSession.SnapshotChanged -= OnSnapshotChanged;
            _workItems!.Writer.TryComplete();
            startSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTask = StopAfterSignalAsync(
                startSignal.Task,
                _lifetimeCancellation!,
                _lifetimeCancellation!.Token);
            stopTask = _stopTask;
        }

        startSignal.SetResult();
        return stopTask.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task StopCoreAsync(CancellationToken lifetimeToken)
    {
        Task? worker;
        CancellationTokenSource? lifetimeCancellation;

        lock (_gate)
        {
            worker = _worker;
            lifetimeCancellation = _lifetimeCancellation;
        }

        try
        {
            if (worker is not null)
            {
                await worker.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
            // Cancelling in-flight and queued commands is the normal process-stop path.
        }
        finally
        {
            try
            {
                // Platform event unregistration is final process cleanup and must still
                // run after a bounded shutdown wait is cancelled.
                await _platform.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                lifetimeCancellation?.Cancel();
                lifetimeCancellation?.Dispose();
                lock (_gate)
                {
                    _workItems = null;
                    _lifetimeCancellation = null;
                    _worker = null;
                }
            }
        }
    }

    private async Task StopAfterSignalAsync(
        Task startSignal,
        CancellationTokenSource lifetimeCancellation,
        CancellationToken lifetimeToken)
    {
        await startSignal.ConfigureAwait(false);

        Exception? cancellationFailure = null;
        try
        {
            lifetimeCancellation.Cancel();
        }
        catch (Exception exception)
        {
            cancellationFailure = exception;
        }

        try
        {
            await StopCoreAsync(lifetimeToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (cancellationFailure is not null)
        {
            throw new AggregateException(cancellationFailure, exception);
        }

        if (cancellationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cancellationFailure).Throw();
        }
    }

    private async Task RunAsync(ChannelReader<WorkItem> reader, CancellationToken cancellationToken)
    {
        await foreach (var workItem in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (workItem.Command is { } command)
            {
                await ExecuteCommandAsync(command, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await PublishMetadataAsync(workItem.Metadata!, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteCommandAsync(
        MediaControlCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (command)
            {
                case MediaControlCommand.Play:
                    await _playbackSession.ResumeAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case MediaControlCommand.Pause:
                    await _playbackSession.PauseAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case MediaControlCommand.Previous:
                    await _playbackSession.PreviousSegmentAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case MediaControlCommand.Next:
                    await _playbackSession.NextSegmentAsync(cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _failureReporter.ReportCommandFailure(command, exception);
        }
    }

    private async Task PublishMetadataAsync(
        MediaControlMetadata metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            await _platform
                .UpdateAsync(metadata, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _failureReporter.ReportMetadataFailure(exception);
        }
    }

    private void OnCommandReceived(object? sender, MediaControlCommand command) =>
        Enqueue(WorkItem.ForCommand(command));

    private void OnSnapshotChanged(object? sender, PlaybackSnapshot snapshot) =>
        EnqueueMetadata(snapshot);

    private void EnqueueMetadata(PlaybackSnapshot snapshot)
    {
        var metadata = MediaControlMetadataProjector.Project(snapshot);
        lock (_gate)
        {
            if (!_started || metadata == _lastQueuedMetadata)
            {
                return;
            }

            _lastQueuedMetadata = metadata;
            _workItems!.Writer.TryWrite(WorkItem.ForMetadata(metadata));
        }
    }

    private void Enqueue(WorkItem workItem)
    {
        lock (_gate)
        {
            if (_started)
            {
                _workItems!.Writer.TryWrite(workItem);
            }
        }
    }

    private sealed record WorkItem(MediaControlCommand? Command, MediaControlMetadata? Metadata)
    {
        public static WorkItem ForCommand(MediaControlCommand command) => new(command, null);

        public static WorkItem ForMetadata(MediaControlMetadata metadata) => new(null, metadata);
    }
}
