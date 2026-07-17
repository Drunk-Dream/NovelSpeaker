namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents the full read model required by the book details experience.
/// </summary>
public sealed record BookDetails(
    string Id,
    string Title,
    string? Author,
    int TotalChapterCount,
    int? CurrentChapterIndex,
    int RemainingChapterCount,
    double OverallProgress,
    bool HasReadingProgress,
    long CachedAudioBytes,
    IReadOnlyList<BookChapterSummary> Chapters);
