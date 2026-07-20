namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Represents one cached chapter row with completeness estimate data.
/// </summary>
public sealed record CachedChapterCacheItem(
    string BookId,
    int ChapterIndex,
    string Title,
    int CachedSegmentCount,
    int EntryCount,
    long TotalSizeBytes,
    int? EstimatedTotalSegmentCount);
