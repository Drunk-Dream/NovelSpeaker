namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents one persisted reading progress snapshot for a book.
/// </summary>
public sealed record ReadingProgressEntry(
    string BookId,
    int ChapterIndex,
    int SegmentIndex,
    int CharacterOffset,
    long AudioPositionMilliseconds,
    DateTimeOffset UpdatedAt);
