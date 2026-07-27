using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.ActiveCache;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.App.WpfTests;

public sealed partial class PlayerViewModelTests
{
    [Fact]
    public void Fixed_duration_command_schedules_timer_without_cancelling_active_cache()
    {
        var coordinator = new FakePlaybackCoordinator(CreatePlayingSnapshot());
        var stopTimer = new FakePlaybackStopTimer();
        var activeCache = new FakeActiveCacheCoordinator();
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(CreateBook(), null),
            activeCacheCoordinator: activeCache,
            stopTimer: stopTimer);

        viewModel.ScheduleStopAfter15MinutesCommand.Execute(null);

        Assert.Equal(PlaybackStopTimerMode.Duration, stopTimer.CurrentSnapshot.Mode);
        Assert.Equal(TimeSpan.FromMinutes(15), stopTimer.CurrentSnapshot.Duration);
        Assert.Equal(0, activeCache.CancelCallCount);
        Assert.True(viewModel.HasActiveStopTimer);
        Assert.Equal("15", viewModel.StopTimerRemainingText);
    }

    [Fact]
    public void Custom_duration_validates_range_and_schedules_valid_value()
    {
        var stopTimer = new FakePlaybackStopTimer();
        var viewModel = CreateViewModel(
            new FakePlaybackCoordinator(CreatePlayingSnapshot()),
            new FakeBookPlaybackContentService(CreateBook(), null),
            stopTimer: stopTimer);

        viewModel.CustomStopMinutesText = "0";
        viewModel.ScheduleCustomStopTimerCommand.Execute(null);

        Assert.Equal("请输入 1 到 1440 分钟。", viewModel.CustomStopTimerErrorText);
        Assert.Equal(PlaybackStopTimerMode.None, stopTimer.CurrentSnapshot.Mode);

        viewModel.CustomStopMinutesText = "125";
        viewModel.ScheduleCustomStopTimerCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.CustomStopTimerErrorText);
        Assert.Equal(TimeSpan.FromMinutes(125), stopTimer.CurrentSnapshot.Duration);
        Assert.Equal("125", viewModel.StopTimerRemainingText);
    }

    [Fact]
    public void Cancel_command_clears_timer_projection()
    {
        var stopTimer = new FakePlaybackStopTimer();
        var viewModel = CreateViewModel(
            new FakePlaybackCoordinator(CreatePlayingSnapshot()),
            new FakeBookPlaybackContentService(CreateBook(), null),
            stopTimer: stopTimer);

        viewModel.ScheduleStopAfter15MinutesCommand.Execute(null);

        Assert.Equal(PlaybackStopTimerMode.Duration, stopTimer.CurrentSnapshot.Mode);
        Assert.Equal("15", viewModel.StopTimerRemainingText);

        viewModel.CancelStopTimerCommand.Execute(null);

        Assert.Equal(PlaybackStopTimerMode.None, stopTimer.CurrentSnapshot.Mode);
        Assert.False(viewModel.HasActiveStopTimer);
        Assert.Equal("—", viewModel.StopTimerRemainingText);
    }

    [Fact]
    public void Stopped_session_cannot_schedule_timer_and_state_changes_refresh_availability()
    {
        var coordinator = new FakePlaybackCoordinator(
            CreatePlayingSnapshot() with { State = PlaybackState.Stopped });
        var stopTimer = new FakePlaybackStopTimer();
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(CreateBook(), null),
            stopTimer: stopTimer);

        Assert.False(viewModel.CanScheduleStopTimer);
        viewModel.ScheduleStopAfter15MinutesCommand.Execute(null);
        Assert.Equal(PlaybackStopTimerMode.None, stopTimer.CurrentSnapshot.Mode);

        coordinator.Publish(coordinator.CurrentSnapshot with { State = PlaybackState.Paused });

        Assert.True(viewModel.CanScheduleStopTimer);
        viewModel.ScheduleStopAfter15MinutesCommand.Execute(null);
        Assert.Equal(PlaybackStopTimerMode.Duration, stopTimer.CurrentSnapshot.Mode);
    }

    [Fact]
    public void Older_timer_event_cannot_overwrite_newer_active_projection()
    {
        var stopTimer = new FakePlaybackStopTimer();
        var viewModel = CreateViewModel(
            new FakePlaybackCoordinator(CreatePlayingSnapshot()),
            new FakeBookPlaybackContentService(CreateBook(), null),
            stopTimer: stopTimer);

        stopTimer.ScheduleAfter(TimeSpan.FromMinutes(30));
        var current = stopTimer.CurrentSnapshot;
        stopTimer.PublishDelayed(new PlaybackStopTimerSnapshot(
            PlaybackStopTimerMode.None,
            null,
            null,
            current.Version - 1));

        Assert.Equal(current, stopTimer.CurrentSnapshot);
        Assert.True(viewModel.HasActiveStopTimer);
        Assert.Equal("30", viewModel.StopTimerRemainingText);
    }

    [Fact]
    public void Remaining_timer_text_refreshes_when_time_provider_advances()
    {
        var timeProvider = new ManualTimeProvider();
        var stopTimer = new FakePlaybackStopTimer(timeProvider);
        var viewModel = CreateViewModel(
            new FakePlaybackCoordinator(CreatePlayingSnapshot()),
            new FakeBookPlaybackContentService(CreateBook(), null),
            stopTimer: stopTimer,
            timeProvider: timeProvider);

        viewModel.OnPageNavigatedTo(CancellationToken.None);
        viewModel.ScheduleStopAfter15MinutesCommand.Execute(null);

        Assert.Equal("15", viewModel.StopTimerRemainingText);

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal("14", viewModel.StopTimerRemainingText);
    }

    private static PlaybackSnapshot CreatePlayingSnapshot() => new(
        PlaybackState.Playing,
        "book-1",
        "示例小说",
        0,
        "第一章",
        0,
        1,
        1,
        "默认规则",
        10,
        0,
        1000,
        null,
        false,
        false);

    private static PlaybackBookContent CreateBook() => new(
        "book-1",
        "示例小说",
        [PlaybackChapterContent.FromLoaded(0, "第一章", [])]);
}
