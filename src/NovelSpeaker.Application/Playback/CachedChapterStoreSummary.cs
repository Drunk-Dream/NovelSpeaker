namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Storage-facing cache totals grouped by book and chapter identifiers.
/// </summary>
public sealed record CachedChapterStoreSummary(
    string BookId,
    int ChapterIndex,
    int DistinctSegmentCount,
    int EntryCount,
    long TotalSizeBytes);
