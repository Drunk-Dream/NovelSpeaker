namespace NovelSpeaker.Application.Cache.ActiveCache;

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
    public double Progress
    {
        get
        {
            if (Status == ActiveCacheBatchStatus.Completed) return 1d;
            if (TotalChapterCount == 0) return 0d;
            // Pending chapters have no segment count until their body is loaded.
            var current = CompletedChapterCount < Chapters.Count ? Chapters[CompletedChapterCount] : null;
            var fraction = current is { TotalSegmentCount: > 0 }
                ? current.CompletedSegmentCount / (double)current.TotalSegmentCount : 0d;
            return Math.Clamp((CompletedChapterCount + fraction) / TotalChapterCount, 0d, 1d);
        }
    }
}
