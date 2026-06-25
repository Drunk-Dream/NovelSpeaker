namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the current playback position that later epics will persist and restore.
/// </summary>
public sealed record PlaybackProgressUpdate(
    string BookId,
    int ChapterIndex,
    int SegmentIndex,
    int CharacterOffset,
    long AudioPositionMilliseconds);
