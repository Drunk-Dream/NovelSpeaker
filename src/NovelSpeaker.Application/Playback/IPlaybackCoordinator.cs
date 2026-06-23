namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Serializes playback commands and exposes a stable snapshot for the desktop UI.
/// </summary>
public interface IPlaybackCoordinator : IAsyncDisposable
{
    PlaybackSnapshot CurrentSnapshot { get; }

    event EventHandler<PlaybackSnapshot>? SnapshotChanged;

    Task StartAsync(PlaybackRequest request, CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task SeekAsync(long positionMilliseconds, CancellationToken cancellationToken);
}
