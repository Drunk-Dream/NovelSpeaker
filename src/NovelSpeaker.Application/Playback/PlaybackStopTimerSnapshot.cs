namespace NovelSpeaker.Application.Playback;

public sealed record PlaybackStopTimerSnapshot(
    PlaybackStopTimerMode Mode,
    DateTimeOffset? DueAt,
    TimeSpan? Duration,
    long Version)
{
    public static PlaybackStopTimerSnapshot None { get; } =
        new(PlaybackStopTimerMode.None, null, null, 0);

    public bool IsActive => Mode != PlaybackStopTimerMode.None;
}
