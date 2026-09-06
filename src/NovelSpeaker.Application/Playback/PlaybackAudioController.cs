namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Owns the local audio resource and bridges its callbacks into the playback session owner.
/// </summary>
internal sealed class PlaybackAudioController : IAsyncDisposable
{
    private readonly ILocalAudioPlaybackCoordinator _localAudio;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private bool _disposed;

    public PlaybackAudioController(ILocalAudioPlaybackCoordinator localAudio)
    {
        _localAudio = localAudio ?? throw new ArgumentNullException(nameof(localAudio));
        _localAudio.SnapshotChanged += OnSnapshotChanged;
        _localAudio.PlaybackCompleted += OnPlaybackCompleted;
        _localAudio.PlaybackFailed += OnPlaybackFailed;
    }

    public LocalAudioPlaybackSnapshot CurrentSnapshot => _localAudio.CurrentSnapshot;

    public double Volume => _localAudio.Volume;

    public event EventHandler<LocalAudioPlaybackSnapshot>? SnapshotChanged;

    public event EventHandler? PlaybackCompleted;

    public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

    public Task StartAsync(LocalAudioPlaybackRequest request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _localAudio.StartAsync(request, cancellationToken);
    }

    public Task ResumeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _localAudio.ResumeAsync(cancellationToken);
    }

    public Task PauseAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _localAudio.PauseAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _localAudio.StopAsync(cancellationToken);
    }

    public Task SeekAsync(long positionMilliseconds, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _localAudio.SeekAsync(positionMilliseconds, cancellationToken);
    }

    public void SetVolume(double volume)
    {
        ThrowIfDisposed();
        _localAudio.SetVolume(volume);
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
        _localAudio.SnapshotChanged -= OnSnapshotChanged;
        _localAudio.PlaybackCompleted -= OnPlaybackCompleted;
        _localAudio.PlaybackFailed -= OnPlaybackFailed;
        await _localAudio.DisposeAsync().ConfigureAwait(false);
    }

    private void OnSnapshotChanged(object? sender, LocalAudioPlaybackSnapshot snapshot)
    {
        if (!_disposed)
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private void OnPlaybackCompleted(object? sender, EventArgs args)
    {
        if (!_disposed)
        {
            PlaybackCompleted?.Invoke(this, args);
        }
    }

    private void OnPlaybackFailed(object? sender, PlaybackErrorEventArgs error)
    {
        if (!_disposed)
        {
            PlaybackFailed?.Invoke(this, error);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
