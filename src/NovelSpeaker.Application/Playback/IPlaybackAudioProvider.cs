namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Produces a local audio file for one playback segment, optionally via cache.
/// </summary>
public interface IPlaybackAudioProvider
{
    Task<PlaybackAudioResult> GetAudioAsync(PlaybackAudioRequest request, CancellationToken cancellationToken);

    Task InvalidateAsync(PlaybackAudioRequest request, CancellationToken cancellationToken);
}
