namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the current global cache footprint shown on cache settings pages.
/// </summary>
public sealed record CacheOverviewModel(
    long TotalSizeBytes,
    int EntryCount,
    long LimitBytes,
    bool IsOverLimit);
