namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the current playback position persisted and restored by playback flows.
/// </summary>
public sealed record PlaybackProgressUpdate(
    string BookId,
    int ChapterIndex,
    int SegmentIndex,
    int CharacterOffset,
    long AudioPositionMilliseconds);
