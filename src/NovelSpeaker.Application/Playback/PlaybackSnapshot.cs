namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the current, UI-facing playback snapshot.
/// </summary>
public sealed record PlaybackSnapshot(
    PlaybackState State,
    string? DisplayTitle,
    string? BookId,
    int ChapterIndex,
    int SegmentIndex,
    long PositionMilliseconds,
    long DurationMilliseconds,
    string? Message,
    bool IsUsingCache)
{
    public static PlaybackSnapshot Idle { get; } = new(
        PlaybackState.Idle,
        null,
        null,
        0,
        0,
        0,
        0,
        "准备播放本地音频。",
        false);
}
