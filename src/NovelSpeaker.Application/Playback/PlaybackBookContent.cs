using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents one imported book projected into playback-ready chapters and segments.
/// </summary>
public sealed record PlaybackBookContent(
    string BookId,
    string BookTitle,
    IReadOnlyList<PlaybackChapterContent> Chapters,
    string? BookAuthor = null);

/// <summary>
/// Represents one playback-ready chapter.
/// </summary>
public sealed record PlaybackChapterContent(
    int ChapterIndex,
    string Title,
    IReadOnlyList<SpeechSegment> Segments);
