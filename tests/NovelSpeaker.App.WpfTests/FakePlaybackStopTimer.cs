using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.App.WpfTests;

internal sealed class FakePlaybackStopTimer : IPlaybackStopTimer
{
    private long _version;

    public PlaybackStopTimerSnapshot CurrentSnapshot { get; private set; } =
        PlaybackStopTimerSnapshot.None;

    public event EventHandler<PlaybackStopTimerSnapshot>? SnapshotChanged;

    public void ScheduleAfter(TimeSpan duration) =>
        Publish(new PlaybackStopTimerSnapshot(
            PlaybackStopTimerMode.Duration,
            DateTimeOffset.UtcNow + duration,
            duration,
            ++_version));

    public void ScheduleAtEndOfSegment() =>
        Publish(new PlaybackStopTimerSnapshot(PlaybackStopTimerMode.EndOfSegment, null, null, ++_version));

    public void ScheduleAtEndOfChapter() =>
        Publish(new PlaybackStopTimerSnapshot(PlaybackStopTimerMode.EndOfChapter, null, null, ++_version));

    public void Cancel() =>
        Publish(new PlaybackStopTimerSnapshot(PlaybackStopTimerMode.None, null, null, ++_version));

    public void PublishDelayed(PlaybackStopTimerSnapshot snapshot)
    {
        _version = Math.Max(_version, snapshot.Version);
        if (snapshot.Version >= CurrentSnapshot.Version)
        {
            CurrentSnapshot = snapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    private void Publish(PlaybackStopTimerSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }
}
