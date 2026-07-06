namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Summarizes cached audio grouped by one chapter within a book.
/// </summary>
public sealed record CachedChapterSummary(
    string BookId,
    int ChapterIndex,
    int DistinctSegmentCount,
    int EntryCount,
    long TotalSizeBytes);
