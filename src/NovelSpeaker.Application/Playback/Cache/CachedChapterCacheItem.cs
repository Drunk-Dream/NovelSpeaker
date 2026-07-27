namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Represents one cached chapter row with current-playback-configuration completeness data.
/// </summary>
public sealed record CachedChapterCacheItem(
    string BookId,
    int ChapterIndex,
    string Title,
    int CachedSegmentCount,
    int EntryCount,
    long TotalSizeBytes,
    int? CurrentConfigurationSegmentCount);
