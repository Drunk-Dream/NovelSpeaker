namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the current state of the low-level local audio playback pipeline.
/// </summary>
public sealed record LocalAudioPlaybackSnapshot(
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
    public static LocalAudioPlaybackSnapshot Idle { get; } = new(
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
