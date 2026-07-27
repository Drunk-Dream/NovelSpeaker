namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Owns the non-persistent stop timer attached to the current playback session.
/// Replacing the playback session cancels the active timer.
/// </summary>
public interface IPlaybackStopTimer
{
    PlaybackStopTimerSnapshot CurrentSnapshot { get; }

    event EventHandler<PlaybackStopTimerSnapshot>? SnapshotChanged;

    void ScheduleAfter(TimeSpan duration);
    void Cancel();
}
