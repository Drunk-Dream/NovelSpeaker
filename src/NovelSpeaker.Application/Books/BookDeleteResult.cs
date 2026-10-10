namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents the outcome of a completed book deletion operation.
/// </summary>
public sealed record BookDeleteResult(
    string BookId,
    int DeletedChapterCount,
    bool DeletedReadingProgress);
