using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.App.WpfTests;

internal sealed class FakePlaybackStopTimer : IPlaybackStopTimer
{
    private readonly TimeProvider _timeProvider;
    private long _version;

    public FakePlaybackStopTimer(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public PlaybackStopTimerSnapshot CurrentSnapshot { get; private set; } =
        PlaybackStopTimerSnapshot.None;

    public event EventHandler<PlaybackStopTimerSnapshot>? SnapshotChanged;

    public void ScheduleAfter(TimeSpan duration) =>
        Publish(new PlaybackStopTimerSnapshot(
            PlaybackStopTimerMode.Duration,
            _timeProvider.GetUtcNow() + duration,
            duration,
            ++_version));

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
