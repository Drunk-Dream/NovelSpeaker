namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Immutable physical cache summary for one book. Current-configuration Coverage is queried separately.
/// </summary>
public sealed record CachedBookSummary(
    string BookId,
    string Title,
    string? Author,
    int ChapterCount,
    int EntryCount,
    long TotalSizeBytes);
