namespace NovelSpeaker.Application.Cache.Audio;

/// <summary>
/// Reports transient generation progress for the active playback segment.
/// </summary>
public sealed record AudioGenerationProgress(
    string Message,
    TimeSpan? RetryAfter = null);
