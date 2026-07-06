namespace NovelSpeaker.Application.Playback;

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
