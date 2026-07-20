namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Owns replaceable prefetch work for isolated playback sessions.
/// </summary>
public interface IPlaybackPrefetchController
{
    Task SubmitAsync(PlaybackPrefetchWindow window, CancellationToken cancellationToken);

    Task CancelAsync(Guid sessionId, CancellationToken cancellationToken);
}
