namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Storage-facing aggregate of the persisted audio cache footprint.
/// </summary>
public sealed record AudioCacheStoreSummary(
    long TotalSizeBytes,
    int EntryCount,
    long LimitBytes,
    bool IsOverLimit);
