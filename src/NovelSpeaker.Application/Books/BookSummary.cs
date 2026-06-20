namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents a minimal book row for the library page.
/// </summary>
public sealed record BookSummary(
    string Id,
    string Title,
    string? Author,
    string CurrentChapterTitle,
    string ImportedAt);
