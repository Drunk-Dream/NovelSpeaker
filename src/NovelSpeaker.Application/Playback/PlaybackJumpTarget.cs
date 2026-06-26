namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Describes a chapter/segment target inside the currently selected book.
/// </summary>
public sealed record PlaybackJumpTarget(
    int ChapterIndex,
    int SegmentIndex);
