namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Immutable physical cache summary for one chapter. Current-configuration Coverage is queried separately.
/// </summary>
public sealed record CachedChapterSummary(
    string BookId,
    int ChapterIndex,
    string Title,
    int DistinctSegmentCount,
    int EntryCount,
    long TotalSizeBytes);
