namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Summarizes the current persisted audio cache footprint.
/// </summary>
public sealed record AudioCacheSummary(
    long TotalSizeBytes,
    int EntryCount,
    long LimitBytes,
    bool IsOverLimit);
