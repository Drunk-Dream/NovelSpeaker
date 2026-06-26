namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Distinguishes user-blocking audio loads from best-effort prefetch work.
/// </summary>
public enum PlaybackAudioPriority
{
    Current = 0,
    Prefetch = 1
}
