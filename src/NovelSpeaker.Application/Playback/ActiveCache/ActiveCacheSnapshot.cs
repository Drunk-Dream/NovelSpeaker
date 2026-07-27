namespace NovelSpeaker.Application.Playback.ActiveCache;

/// <summary>
/// Projects safe, read-only progress for the latest active-cache batch.
/// </summary>
public sealed record ActiveCacheSnapshot(
    Guid BatchId,
    string BookId,
    string BookTitle,
    ActiveCacheBatchStatus Status,
    int CompletedChapterCount,
    int TotalChapterCount,
    int CompletedSegmentCount,
    int TotalSegmentCount,
    int? CurrentChapterIndex,
    string? CurrentChapterTitle,
    IReadOnlyList<ActiveCacheChapterSnapshot> Chapters,
    string? ErrorSummary)
{
    public double Progress =>
        TotalSegmentCount == 0
            ? Status == ActiveCacheBatchStatus.Completed ? 1d : 0d
            : (double)CompletedSegmentCount / TotalSegmentCount;
}
