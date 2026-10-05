namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the user-visible playback state for the current audio session.
/// </summary>
public enum PlaybackState
{
    Idle = 0,
    Preparing = 1,
    Playing = 3,
    Paused = 4,
    Stopped = 5,
    Recovering = 6,
    Faulted = 7
}
