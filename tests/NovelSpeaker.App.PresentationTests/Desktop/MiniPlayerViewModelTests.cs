using Microsoft.Extensions.Logging.Abstractions;
using NovelSpeaker.App.Desktop.MiniPlayer;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Desktop;

public sealed class MiniPlayerViewModelTests
{
    [Fact]
    public async Task Snapshot_is_projected_and_commands_use_the_shared_playback_session()
    {
        var playback = new FakePlaybackSession();
        var viewModel = CreateViewModel(playback);
        playback.Publish(PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Playing,
            BookId = "book-1",
            BookTitle = "测试书",
            ChapterTitle = "第二章",
            SegmentIndex = 1,
            SegmentCount = 3,
            PositionMilliseconds = 250,
            DurationMilliseconds = 1000,
            Volume = 0.4
        });

        Assert.Equal("测试书", viewModel.BookTitle);
        Assert.Equal("第二章", viewModel.ChapterTitle);
        Assert.Equal(1, viewModel.SegmentProgressValue);
        Assert.Equal(2, viewModel.SegmentProgressMaximum);
        Assert.True(viewModel.CanGoToPreviousSegment);
        Assert.True(viewModel.CanGoToNextSegment);
        Assert.Equal("暂停", viewModel.PlaybackActionText);
        Assert.Equal(0.4, viewModel.Volume);
        Assert.Equal("40%", viewModel.VolumePercentText);
        Assert.Equal("置顶", viewModel.TopmostActionText);
        viewModel.IsTopmost = true;
        Assert.Equal("取消置顶", viewModel.TopmostActionText);

        viewModel.Volume = 0.25;
        Assert.Equal(0.25, playback.LastVolume);

        viewModel.ToggleVolumeMenuCommand.Execute(null);
        Assert.True(viewModel.IsVolumeMenuOpen);
        viewModel.ToggleVolumeMenuCommand.Execute(null);
        Assert.False(viewModel.IsVolumeMenuOpen);

        await viewModel.TogglePlaybackCommand.ExecuteAsync(null);
        await viewModel.PreviousChapterCommand.ExecuteAsync(null);
        await viewModel.NextChapterCommand.ExecuteAsync(null);
        await viewModel.PreviousSegmentCommand.ExecuteAsync(null);
        await viewModel.NextSegmentCommand.ExecuteAsync(null);

        Assert.Equal(["pause", "previous-chapter", "next-chapter", "previous-segment", "next-segment"], playback.Calls);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task Segment_progress_commit_jumps_to_the_selected_segment_and_updates_tooltip_projection()
    {
        var playback = new FakePlaybackSession();
        playback.Publish(PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Paused,
            BookId = "book-1",
            ChapterIndex = 2,
            SegmentIndex = 0,
            SegmentCount = 3
        });
        var viewModel = CreateViewModel(playback);

        viewModel.BeginSegmentProgressInteraction();
        viewModel.PreviewSegmentProgress(2);

        Assert.Equal("3 / 3", viewModel.DisplayedSegmentCounterText);
        await viewModel.CommitSegmentProgressAsync(2, CancellationToken.None);

        Assert.Equal((2, 2), playback.LastJumpedSegment);
        Assert.False(viewModel.IsSegmentProgressDragging);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task Position_and_topmost_are_coalesced_and_final_state_is_persisted()
    {
        var timeProvider = new ManualTimeProvider();
        var settings = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(new FakePlaybackSession(), settings, timeProvider);

        viewModel.UpdateWindowPosition(10, 20);
        viewModel.UpdateWindowPosition(30, 40);
        viewModel.IsTopmost = true;

        Assert.Empty(settings.Updates);
        timeProvider.Advance(MiniPlayerViewModel.PlacementSaveDelay);
        await settings.UpdateObserved.Task;

        var update = Assert.Single(settings.Updates);
        Assert.Equal(30, update.MiniPlayerLeft);
        Assert.Equal(40, update.MiniPlayerTop);
        Assert.True(update.MiniPlayerTopmost);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task Flush_cancels_throttle_and_saves_latest_placement_once_before_shutdown()
    {
        var timeProvider = new ManualTimeProvider();
        var settings = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(new FakePlaybackSession(), settings, timeProvider);
        viewModel.UpdateWindowPosition(15, 25);

        await viewModel.FlushPlacementAsync(CancellationToken.None);
        timeProvider.Advance(MiniPlayerViewModel.PlacementSaveDelay);

        var update = Assert.Single(settings.Updates);
        Assert.Equal(15, update.MiniPlayerLeft);
        Assert.Equal(25, update.MiniPlayerTop);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_cancels_and_drains_owned_snapshot_projection()
    {
        var playback = new FakePlaybackSession();
        var scheduler = new GatedUiScheduler();
        var viewModel = new MiniPlayerViewModel(
            playback,
            new FakeSettingsService(AppSettings.Default),
            scheduler,
            NullLogger<MiniPlayerViewModel>.Instance);
        playback.Publish(PlaybackSnapshot.Idle with { BookId = "book-1", BookTitle = "待投影" });

        var disposeTask = viewModel.DisposeAsync().AsTask();

        Assert.False(disposeTask.IsCompleted);
        scheduler.Release();
        await disposeTask;
    }

    private static MiniPlayerViewModel CreateViewModel(
        FakePlaybackSession playback,
        FakeSettingsService? settings = null,
        TimeProvider? timeProvider = null) =>
        new(
            playback,
            settings ?? new FakeSettingsService(AppSettings.Default),
            new InlineUiScheduler(),
            NullLogger<MiniPlayerViewModel>.Instance,
            timeProvider);

    private sealed class InlineUiScheduler : IUiScheduler
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }
    }

    private sealed class GatedUiScheduler : IUiScheduler
    {
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CheckAccess() => false;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) =>
            InvokeAsync(
                () =>
                {
                    action();
                    return Task.CompletedTask;
                },
                cancellationToken);

        public async Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await _gate.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await action().ConfigureAwait(false);
        }

        public void Release() => _gate.TrySetResult();
    }

    private sealed class FakeSettingsService(AppSettings settings) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = settings;

        public List<AppSettingsUpdate> Updates { get; } = [];

        public TaskCompletionSource UpdateObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<AppSettingsChangedEventArgs>? Changed;

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Updates.Add(update);
            var previous = Current;
            Current = Current with
            {
                MiniPlayerLeft = update.ClearMiniPlayerLeft ? null : update.MiniPlayerLeft ?? Current.MiniPlayerLeft,
                MiniPlayerTop = update.ClearMiniPlayerTop ? null : update.MiniPlayerTop ?? Current.MiniPlayerTop,
                MiniPlayerTopmost = update.MiniPlayerTopmost ?? Current.MiniPlayerTopmost
            };
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, Current));
            UpdateObserved.TrySetResult();
            return Task.FromResult(Current);
        }
    }

    private sealed class FakePlaybackSession : IPlaybackSession
    {
        public PlaybackSnapshot CurrentSnapshot { get; private set; } = PlaybackSnapshot.Idle;

        public List<string> Calls { get; } = [];

        public (int ChapterIndex, int SegmentIndex)? LastJumpedSegment { get; private set; }

        public double? LastVolume { get; private set; }

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public Task PauseAsync(CancellationToken cancellationToken) => Record("pause", cancellationToken);
        public Task ResumeAsync(CancellationToken cancellationToken) => Record("resume", cancellationToken);
        public Task PreviousChapterAsync(CancellationToken cancellationToken) => Record("previous-chapter", cancellationToken);
        public Task NextChapterAsync(CancellationToken cancellationToken) => Record("next-chapter", cancellationToken);
        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => Record("previous-segment", cancellationToken);
        public Task NextSegmentAsync(CancellationToken cancellationToken) => Record("next-segment", cancellationToken);

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken)
        {
            LastJumpedSegment = (chapterIndex, segmentIndex);
            return Task.CompletedTask;
        }
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;
        public void SetVolume(double volume) => LastVolume = volume;

        private Task Record(string call, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(call);
            return Task.CompletedTask;
        }
    }
}
