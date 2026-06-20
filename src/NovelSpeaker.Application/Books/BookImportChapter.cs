namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents one chapter found during import analysis before database IDs exist.
/// </summary>
public sealed record BookImportChapter(
    int ChapterIndex,
    string Title,
    string Content,
    int StartOffset,
    int Length);
