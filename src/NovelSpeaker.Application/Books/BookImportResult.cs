namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reports the persisted book identity after a successful import.
/// </summary>
public sealed record BookImportResult(
    string BookId,
    string Title,
    int ChapterCount);
