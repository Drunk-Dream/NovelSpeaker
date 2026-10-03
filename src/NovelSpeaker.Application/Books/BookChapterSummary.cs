namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents one read-only chapter row for the book details page.
/// </summary>
public sealed record BookChapterSummary(
    int ChapterIndex,
    string Title,
    string? ChapterId = null,
    ActiveSourceContext? SourceContext = null);
