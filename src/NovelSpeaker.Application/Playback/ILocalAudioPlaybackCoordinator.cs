namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Serializes playback of one local audio file at a time and reports local playback state.
/// </summary>
public interface ILocalAudioPlaybackCoordinator : IAsyncDisposable
{
    LocalAudioPlaybackSnapshot CurrentSnapshot { get; }

    event EventHandler<LocalAudioPlaybackSnapshot>? SnapshotChanged;

    event EventHandler? PlaybackCompleted;

    event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

    Task StartAsync(LocalAudioPlaybackRequest request, CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task SeekAsync(long positionMilliseconds, CancellationToken cancellationToken);
}
