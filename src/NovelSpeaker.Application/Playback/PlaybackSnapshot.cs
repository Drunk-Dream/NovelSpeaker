namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the current UI-facing view of the active book playback session.
/// </summary>
public sealed record PlaybackSnapshot(
    PlaybackState State,
    string? BookId,
    string? BookTitle,
    int ChapterIndex,
    string? ChapterTitle,
    int SegmentIndex,
    int SegmentCount,
    long? RuleId,
    string? RuleName,
    int SpeakSpeed,
    long PositionMilliseconds,
    long DurationMilliseconds,
    string? Message,
    bool IsUsingCache,
    bool CanRetry,
    bool CanSkip,
    string? BookAuthor = null,
    bool HasAvailableRule = true)
{
    public static PlaybackSnapshot Idle { get; } = new(
        PlaybackState.Idle,
        null,
        null,
        0,
        null,
        0,
        0,
        null,
        null,
        10,
        0,
        0,
        "请选择一本书并开始播放。",
        false,
        false,
        false,
        null,
        true);
}
