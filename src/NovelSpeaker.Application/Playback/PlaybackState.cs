namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the user-visible playback state for the current audio session.
/// </summary>
public enum PlaybackState
{
    Idle,
    Preparing,
    Buffering,
    Playing,
    Paused,
    Stopped,
    Recovering,
    Faulted
}
