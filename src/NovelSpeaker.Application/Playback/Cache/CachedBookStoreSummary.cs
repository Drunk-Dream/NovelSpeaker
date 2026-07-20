namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Storage-facing cache totals grouped by imported book identifier.
/// </summary>
public sealed record CachedBookStoreSummary(
    string BookId,
    int ChapterCount,
    int EntryCount,
    long TotalSizeBytes);
