namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Summarizes cached audio grouped by one imported book.
/// </summary>
public sealed record CachedBookSummary(
    string BookId,
    int ChapterCount,
    int EntryCount,
    long TotalSizeBytes);
