using System.Collections.Concurrent;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackStopTimerControllerTests
{
    [Fact]
    public async Task Duration_uses_time_provider_and_pauses_once()
    {
        var timeProvider = new ManualTimeProvider();
        var pauseReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pauseCallCount = 0;
        await using var controller = new PlaybackStopTimerController(
            timeProvider,
            _ =>
            {
                pauseCallCount++;
                pauseReached.TrySetResult();
                return Task.CompletedTask;
            },
            () => { });

        controller.ScheduleAfter(TimeSpan.FromMinutes(15));
        timeProvider.Advance(TimeSpan.FromMinutes(14));

        Assert.Equal(0, pauseCallCount);
        Assert.Equal(PlaybackStopTimerMode.Duration, controller.CurrentSnapshot.Mode);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await pauseReached.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, pauseCallCount);
        Assert.Equal(PlaybackStopTimerMode.None, controller.CurrentSnapshot.Mode);
    }

    [Fact]
    public async Task Replacing_duration_cancels_the_previous_deadline()
    {
        var timeProvider = new ManualTimeProvider();
        var pauseReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pauseCallCount = 0;
        await using var controller = new PlaybackStopTimerController(
            timeProvider,
            _ =>
            {
                pauseCallCount++;
                pauseReached.TrySetResult();
                return Task.CompletedTask;
            },
            () => { });

        controller.ScheduleAfter(TimeSpan.FromMinutes(15));
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        controller.ScheduleAfter(TimeSpan.FromMinutes(30));
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(0, pauseCallCount);

        timeProvider.Advance(TimeSpan.FromMinutes(25));
        await pauseReached.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, pauseCallCount);
    }

    [Fact]
    public async Task Cancel_prevents_duration_from_pausing()
    {
        var timeProvider = new ManualTimeProvider();
        var pauseCallCount = 0;
        await using var controller = new PlaybackStopTimerController(
            timeProvider,
            _ =>
            {
                pauseCallCount++;
                return Task.CompletedTask;
            },
            () => { });

        controller.ScheduleAfter(TimeSpan.FromMinutes(15));
        controller.Cancel();
        timeProvider.Advance(TimeSpan.FromHours(1));
        await controller.WaitForPendingOperationAsync();

        Assert.Equal(0, pauseCallCount);
        Assert.Equal(PlaybackStopTimerMode.None, controller.CurrentSnapshot.Mode);
    }

    [Fact]
    public async Task Pause_failure_is_reported_without_faulting_detached_work()
    {
        var timeProvider = new ManualTimeProvider();
        var failureReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new PlaybackStopTimerController(
            timeProvider,
            _ => Task.FromException(new InvalidOperationException("sensitive-detail")),
            () => failureReported.TrySetResult());

        controller.ScheduleAfter(TimeSpan.FromMinutes(15));
        timeProvider.Advance(TimeSpan.FromMinutes(15));

        await failureReported.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await controller.WaitForPendingOperationAsync();
        Assert.Equal(PlaybackStopTimerMode.None, controller.CurrentSnapshot.Mode);
    }

    [Fact]
    public async Task Replaced_timer_keeps_new_current_snapshot_when_old_expiry_event_arrives_late()
    {
        var timeProvider = new ManualTimeProvider();
        var oldExpiryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldExpiry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedVersions = new ConcurrentQueue<long>();
        await using var controller = new PlaybackStopTimerController(
            timeProvider,
            _ => Task.CompletedTask,
            () => { });
        controller.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.Mode == PlaybackStopTimerMode.None && snapshot.Version == 2)
            {
                oldExpiryEntered.TrySetResult();
                releaseOldExpiry.Task.GetAwaiter().GetResult();
            }

            observedVersions.Enqueue(snapshot.Version);
        };

        controller.ScheduleAfter(TimeSpan.FromMinutes(15));
        var advance = Task.Run(() => timeProvider.Advance(TimeSpan.FromMinutes(15)));
        await oldExpiryEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        controller.ScheduleAfter(TimeSpan.FromMinutes(30));
        var current = controller.CurrentSnapshot;
        releaseOldExpiry.TrySetResult();
        await advance.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(PlaybackStopTimerMode.Duration, current.Mode);
        Assert.Equal(TimeSpan.FromMinutes(30), current.Duration);
        Assert.Equal(3, current.Version);
        Assert.Equal(current, controller.CurrentSnapshot);
        Assert.Equal([1L, 3L, 2L], observedVersions);
    }

    [Fact]
    public async Task Segment_boundary_consumes_segment_mode()
    {
        await using var controller = new PlaybackStopTimerController(
            TimeProvider.System,
            _ => Task.CompletedTask,
            () => { });
        controller.ScheduleAtEndOfSegment();

        var shouldPause = controller.TryConsumeBoundary(chapterEnded: false);

        Assert.True(shouldPause);
        Assert.Equal(PlaybackStopTimerMode.None, controller.CurrentSnapshot.Mode);
    }

    [Fact]
    public async Task Chapter_boundary_ignores_segments_until_chapter_ends()
    {
        await using var controller = new PlaybackStopTimerController(
            TimeProvider.System,
            _ => Task.CompletedTask,
            () => { });
        controller.ScheduleAtEndOfChapter();

        Assert.False(controller.TryConsumeBoundary(chapterEnded: false));
        Assert.Equal(PlaybackStopTimerMode.EndOfChapter, controller.CurrentSnapshot.Mode);

        Assert.True(controller.TryConsumeBoundary(chapterEnded: true));
        Assert.Equal(PlaybackStopTimerMode.None, controller.CurrentSnapshot.Mode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public async Task Duration_rejects_out_of_range_minutes(int minutes)
    {
        await using var controller = new PlaybackStopTimerController(
            TimeProvider.System,
            _ => Task.CompletedTask,
            () => { });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            controller.ScheduleAfter(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public async Task Duration_rejects_less_than_one_minute()
    {
        await using var controller = new PlaybackStopTimerController(
            TimeProvider.System,
            _ => Task.CompletedTask,
            () => { });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            controller.ScheduleAfter(TimeSpan.FromSeconds(59)));
    }
}
