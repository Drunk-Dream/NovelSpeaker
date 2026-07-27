namespace NovelSpeaker.Application.Playback.ActiveCache;

/// <summary>
/// Projects progress for one frozen chapter without exposing its text.
/// </summary>
public sealed record ActiveCacheChapterSnapshot(
    int ChapterIndex,
    string ChapterTitle,
    int CompletedSegmentCount,
    int TotalSegmentCount,
    ActiveCacheChapterStatus Status,
    string? ErrorSummary);
