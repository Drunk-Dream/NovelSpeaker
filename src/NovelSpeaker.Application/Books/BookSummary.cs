namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents a minimal book row for the library page.
/// </summary>
public sealed record BookSummary(
    string Id,
    string Title,
    string? Author,
    string CurrentChapterTitle,
    DateTimeOffset ImportedAt,
    DateTimeOffset? LastPlayedAt = null,
    int TotalChapterCount = 0,
    int? CurrentChapterIndex = null,
    int RemainingChapterCount = 0,
    double OverallProgress = 0,
    bool HasReadingProgress = false);
