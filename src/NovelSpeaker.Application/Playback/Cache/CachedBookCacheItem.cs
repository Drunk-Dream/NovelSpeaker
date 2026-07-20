namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Represents one cached book row in the cache workspace.
/// </summary>
public sealed record CachedBookCacheItem(
    string BookId,
    string Title,
    string? Author,
    int ChapterCount,
    int EntryCount,
    long TotalSizeBytes);
