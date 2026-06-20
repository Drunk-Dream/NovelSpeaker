namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents a chapter persisted for a specific imported book.
/// </summary>
public sealed record Chapter(
    string Id,
    string BookId,
    int ChapterIndex,
    string Title,
    string Content,
    int StartOffset,
    int Length);
