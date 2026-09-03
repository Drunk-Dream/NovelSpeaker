using System.Threading.Channels;
using NovelSpeaker.Application.Playback.Audio;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Coordinates one local audio player and exposes serialized low-level playback snapshots.
/// </summary>
public sealed class LocalAudioPlaybackCoordinator : ILocalAudioPlaybackCoordinator
{
    private readonly IAudioPlayer _audioPlayer;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Channel<PlayerEventCommand> _playerEvents = Channel.CreateUnbounded<PlayerEventCommand>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly CancellationTokenSource _playerEventCancellation = new();
    private readonly Task _playerEventProcessor;
    private readonly object _disposeGate = new();
    private LocalAudioPlaybackRequest? _currentRequest;
    private LocalAudioPlaybackSnapshot _currentSnapshot = LocalAudioPlaybackSnapshot.Idle;
    private long _sessionVersion;
    private double _volume = PlaybackVolume.Default;
    private bool _disposed;
    private EventHandler? _playbackCompletedHandler;
    private EventHandler<PlaybackErrorEventArgs>? _playbackFailedHandler;
    private Task? _disposeTask;

    public LocalAudioPlaybackCoordinator(IAudioPlayer audioPlayer)
    {
        _audioPlayer = audioPlayer;
        _playerEventProcessor = ProcessPlayerEventsAsync();
    }

    public LocalAudioPlaybackSnapshot CurrentSnapshot => _currentSnapshot;

    public double Volume => _volume;

    public event EventHandler<LocalAudioPlaybackSnapshot>? SnapshotChanged;

    public event EventHandler? PlaybackCompleted;

    public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

    public void SetVolume(double volume)
    {
        ThrowIfDisposed();

        var normalized = PlaybackVolume.Normalize(volume);
        if (normalized == _volume)
        {
            return;
        }

        _volume = normalized;
        _audioPlayer.Volume = normalized;
        PublishSnapshot(_currentSnapshot with { Volume = normalized });
    }

    public async Task StartAsync(LocalAudioPlaybackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifecycleCancellation.Token);
        await _mutex.WaitAsync(linkedCancellation.Token);
        try
        {
            ThrowIfDisposed();
            _sessionVersion++;
            _currentRequest = request;
            DetachPlayerHandlers();

            PublishSnapshot(CreateSnapshot(
                PlaybackState.Preparing,
                request.DisplayTitle,
                request.BookId,
                request.ChapterIndex,
                request.SegmentIndex,
                request.ResumePositionMilliseconds,
                0,
                "正在准备本地音频。",
                request.IsUsingCache,
                request.PlaybackSessionId));

            _audioPlayer.Stop();
            await _audioPlayer.LoadAsync(request.FilePath, linkedCancellation.Token);
            AttachPlayerHandlers(_sessionVersion);

            if (request.ResumePositionMilliseconds > 0)
            {
                _audioPlayer.Seek(TimeSpan.FromMilliseconds(request.ResumePositionMilliseconds));
            }

            _audioPlayer.Play();
            PublishSnapshot(CreateSnapshot(
                PlaybackState.Playing,
                request.DisplayTitle,
                request.BookId,
                request.ChapterIndex,
                request.SegmentIndex,
                ToMilliseconds(_audioPlayer.Position),
                ToMilliseconds(_audioPlayer.Duration),
                null,
                request.IsUsingCache,
                request.PlaybackSessionId));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishFailure(PlaybackErrorMapper.Map(exception));
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifecycleCancellation.Token);
        await _mutex.WaitAsync(linkedCancellation.Token);
        try
        {
            ThrowIfDisposed();
            if (_currentRequest is null)
            {
                return;
            }

            if (_audioPlayer.Duration > TimeSpan.Zero && _audioPlayer.Position >= _audioPlayer.Duration)
            {
                _audioPlayer.Seek(TimeSpan.Zero);
            }

            _audioPlayer.Play();
            PublishSnapshotFromPlayer(PlaybackState.Playing, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishFailure(PlaybackErrorMapper.Map(exception));
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifecycleCancellation.Token);
        await _mutex.WaitAsync(linkedCancellation.Token);
        try
        {
            ThrowIfDisposed();
            if (_currentRequest is null)
            {
                return;
            }

            _audioPlayer.Pause();
            PublishSnapshotFromPlayer(PlaybackState.Paused, null);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifecycleCancellation.Token);
        await _mutex.WaitAsync(linkedCancellation.Token);
        try
        {
            ThrowIfDisposed();
            if (_currentRequest is null)
            {
                PublishSnapshot(LocalAudioPlaybackSnapshot.Idle);
                return;
            }

            _audioPlayer.Stop();
            PublishSnapshotFromPlayer(PlaybackState.Stopped, "已停止本地音频播放。");
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SeekAsync(long positionMilliseconds, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifecycleCancellation.Token);
        await _mutex.WaitAsync(linkedCancellation.Token);
        try
        {
            ThrowIfDisposed();
            if (_currentRequest is null)
            {
                return;
            }

            _audioPlayer.Seek(TimeSpan.FromMilliseconds(positionMilliseconds));
            PublishSnapshotFromPlayer(_currentSnapshot.State, null);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        _lifecycleCancellation.Cancel();
        _playerEventCancellation.Cancel();
        _playerEvents.Writer.TryComplete();
        DetachPlayerHandlers();
        await _mutex.WaitAsync().ConfigureAwait(false);
        _mutex.Release();
        await _playerEventProcessor.ConfigureAwait(false);
        await _audioPlayer.DisposeAsync();
        _playerEventCancellation.Dispose();
        _lifecycleCancellation.Dispose();
    }

    private void AttachPlayerHandlers(long sessionVersion)
    {
        _playbackCompletedHandler = (_, _) => EnqueuePlayerEvent(new PlayerEventCommand(
            PlayerEventCommandKind.Completed,
            sessionVersion,
            null));
        _playbackFailedHandler = (_, error) => EnqueuePlayerEvent(new PlayerEventCommand(
            PlayerEventCommandKind.Failed,
            sessionVersion,
            error));

        _audioPlayer.PlaybackCompleted += _playbackCompletedHandler;
        _audioPlayer.PlaybackFailed += _playbackFailedHandler;
    }

    private void DetachPlayerHandlers()
    {
        if (_playbackCompletedHandler is not null)
        {
            _audioPlayer.PlaybackCompleted -= _playbackCompletedHandler;
            _playbackCompletedHandler = null;
        }

        if (_playbackFailedHandler is not null)
        {
            _audioPlayer.PlaybackFailed -= _playbackFailedHandler;
            _playbackFailedHandler = null;
        }
    }

    private void EnqueuePlayerEvent(PlayerEventCommand command)
    {
        if (!_disposed)
        {
            _playerEvents.Writer.TryWrite(command);
        }
    }

    private async Task ProcessPlayerEventsAsync()
    {
        try
        {
            await foreach (var command in _playerEvents.Reader.ReadAllAsync(_playerEventCancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    await _mutex.WaitAsync(_lifecycleCancellation.Token).ConfigureAwait(false);
                    try
                    {
                        if (_disposed || command.SessionVersion != _sessionVersion || _currentRequest is null)
                        {
                            continue;
                        }

                        switch (command.Kind)
                        {
                            case PlayerEventCommandKind.Completed:
                                PublishSnapshotFromPlayer(PlaybackState.Stopped, "当前音频已播放完成。");
                                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
                                break;
                            case PlayerEventCommandKind.Failed:
                                PublishFailure(command.Error!);
                                PlaybackFailed?.Invoke(this, command.Error!);
                                break;
                        }
                    }
                    finally
                    {
                        _mutex.Release();
                    }
                }
                catch (OperationCanceledException) when (_disposed || _playerEventCancellation.IsCancellationRequested)
                {
                    // Closing cancels the owned player-event processor.
                }
                catch (Exception)
                {
                    // Player-event subscribers are outside this coordinator's ownership boundary.
                    // Keep the processor observed and continue handling later events.
                }
            }
        }
        catch (OperationCanceledException) when (_playerEventCancellation.IsCancellationRequested)
        {
            // Closing cancels the owned player-event processor.
        }
    }

    private enum PlayerEventCommandKind
    {
        Completed,
        Failed
    }

    private sealed record PlayerEventCommand(
        PlayerEventCommandKind Kind,
        long SessionVersion,
        PlaybackErrorEventArgs? Error);

    private void PublishFailure(PlaybackErrorEventArgs error)
    {
        if (_currentRequest is null)
        {
            PublishSnapshot(LocalAudioPlaybackSnapshot.Idle with
            {
                State = PlaybackState.Faulted,
                Message = error.Message
            });
            return;
        }

        PublishSnapshot(CreateSnapshot(
            PlaybackState.Faulted,
            _currentRequest.DisplayTitle,
            _currentRequest.BookId,
            _currentRequest.ChapterIndex,
            _currentRequest.SegmentIndex,
            ToMilliseconds(_audioPlayer.Position),
            ToMilliseconds(_audioPlayer.Duration),
            error.Message,
            _currentRequest.IsUsingCache,
            _currentRequest.PlaybackSessionId));
    }

    private void PublishSnapshotFromPlayer(PlaybackState state, string? message)
    {
        if (_currentRequest is null)
        {
            PublishSnapshot(LocalAudioPlaybackSnapshot.Idle);
            return;
        }

        PublishSnapshot(CreateSnapshot(
            state,
            _currentRequest.DisplayTitle,
            _currentRequest.BookId,
            _currentRequest.ChapterIndex,
            _currentRequest.SegmentIndex,
            ToMilliseconds(_audioPlayer.Position),
            ToMilliseconds(_audioPlayer.Duration),
            message,
            _currentRequest.IsUsingCache,
            _currentRequest.PlaybackSessionId));
    }

    private void PublishSnapshot(LocalAudioPlaybackSnapshot snapshot)
    {
        _currentSnapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private LocalAudioPlaybackSnapshot CreateSnapshot(
        PlaybackState state,
        string? displayTitle,
        string? bookId,
        int chapterIndex,
        int segmentIndex,
        long positionMilliseconds,
        long durationMilliseconds,
        string? message,
        bool isUsingCache,
        Guid? playbackSessionId)
    {
        return new LocalAudioPlaybackSnapshot(
            state,
            displayTitle,
            bookId,
            chapterIndex,
            segmentIndex,
            positionMilliseconds,
            durationMilliseconds,
            message,
            isUsingCache,
            _volume,
            playbackSessionId);
    }

    private static long ToMilliseconds(TimeSpan timeSpan)
    {
        return Convert.ToInt64(Math.Max(0, timeSpan.TotalMilliseconds));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
