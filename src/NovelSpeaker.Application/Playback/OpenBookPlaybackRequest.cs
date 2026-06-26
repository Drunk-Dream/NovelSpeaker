namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Describes where a paused playback context should open for one book.
/// </summary>
public sealed record OpenBookPlaybackRequest(
    string BookId,
    int? ChapterIndex,
    int? SegmentIndex,
    int? SpeakSpeedOverride);
