namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Reports transient generation progress for the active playback segment.
/// </summary>
public sealed record PlaybackAudioProgress(
    string Message,
    TimeSpan? RetryAfter = null);
