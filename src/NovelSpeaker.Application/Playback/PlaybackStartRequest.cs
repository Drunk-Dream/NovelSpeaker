namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Describes where a book-oriented playback session should start.
/// </summary>
public sealed record PlaybackStartRequest(
    string BookId,
    int? ChapterIndex,
    int? SegmentIndex,
    long? ResumePositionMilliseconds,
    int? SpeakSpeedOverride);
