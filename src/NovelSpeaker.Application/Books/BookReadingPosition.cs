namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents the persisted reading position for a single book.
/// </summary>
public sealed record BookReadingPosition(
    string BookId,
    int ChapterIndex,
    int SegmentIndex,
    int CharacterOffset,
    long AudioPositionMilliseconds,
    DateTimeOffset UpdatedAt);
