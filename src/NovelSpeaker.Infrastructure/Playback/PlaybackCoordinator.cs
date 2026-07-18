using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Audio;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Coordinates one local audio player and exposes serialized low-level playback snapshots.
/// </summary>
public sealed class LocalAudioPlaybackCoordinator : ILocalAudioPlaybackCoordinator
{
    private readonly IAudioPlayer _audioPlayer;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private LocalAudioPlaybackRequest? _currentRequest;
    private LocalAudioPlaybackSnapshot _currentSnapshot = LocalAudioPlaybackSnapshot.Idle;
    private long _sessionVersion;
    private bool _disposed;
    private EventHandler? _playbackCompletedHandler;
    private EventHandler<PlaybackErrorEventArgs>? _playbackFailedHandler;

    public LocalAudioPlaybackCoordinator(IAudioPlayer audioPlayer)
    {
        _audioPlayer = audioPlayer;
    }

    public LocalAudioPlaybackSnapshot CurrentSnapshot => _currentSnapshot;

    public event EventHandler<LocalAudioPlaybackSnapshot>? SnapshotChanged;

    public event EventHandler? PlaybackCompleted;

    public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

    public async Task StartAsync(LocalAudioPlaybackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        await _mutex.WaitAsync(cancellationToken);
        try
        {
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
                request.IsUsingCache));

            _audioPlayer.Stop();
            await _audioPlayer.LoadAsync(request.FilePath, cancellationToken);
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
                request.IsUsingCache));
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
        await _mutex.WaitAsync(cancellationToken);
        try
        {
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
        await _mutex.WaitAsync(cancellationToken);
        try
        {
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
        await _mutex.WaitAsync(cancellationToken);
        try
        {
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
        await _mutex.WaitAsync(cancellationToken);
        try
        {
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachPlayerHandlers();
        await _audioPlayer.DisposeAsync();
    }

    private void AttachPlayerHandlers(long sessionVersion)
    {
        _playbackCompletedHandler = (_, _) => _ = HandlePlaybackCompletedAsync(sessionVersion);
        _playbackFailedHandler = (_, error) => _ = HandlePlaybackFailedAsync(sessionVersion, error);

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

    private async Task HandlePlaybackCompletedAsync(long sessionVersion)
    {
        if (_disposed)
        {
            return;
        }

        await _mutex.WaitAsync();
        try
        {
            if (sessionVersion != _sessionVersion || _currentRequest is null)
            {
                return;
            }

            PublishSnapshotFromPlayer(PlaybackState.Stopped, "当前音频已播放完成。");
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task HandlePlaybackFailedAsync(long sessionVersion, PlaybackErrorEventArgs error)
    {
        if (_disposed)
        {
            return;
        }

        await _mutex.WaitAsync();
        try
        {
            if (sessionVersion != _sessionVersion)
            {
                return;
            }

            PublishFailure(error);
            PlaybackFailed?.Invoke(this, error);
        }
        finally
        {
            _mutex.Release();
        }
    }

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
            _currentRequest.IsUsingCache));
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
            _currentRequest.IsUsingCache));
    }

    private void PublishSnapshot(LocalAudioPlaybackSnapshot snapshot)
    {
        _currentSnapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private static LocalAudioPlaybackSnapshot CreateSnapshot(
        PlaybackState state,
        string? displayTitle,
        string? bookId,
        int chapterIndex,
        int segmentIndex,
        long positionMilliseconds,
        long durationMilliseconds,
        string? message,
        bool isUsingCache)
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
            isUsingCache);
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
