using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Playback;
using NovelSpeaker.App.ViewModels;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class PlayerViewModelTests
{
    [Fact]
    public void Constructor_projects_existing_snapshot()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
            "内置演示 WAV",
            "demo-book",
            0,
            0,
            200,
            700,
            null,
            false));

        var viewModel = new PlayerViewModel(coordinator, new FakePlaybackDemoRequestFactory());

        Assert.Equal("内置演示 WAV", viewModel.CurrentTitle);
        Assert.Equal("正在播放", viewModel.StatusText);
        Assert.Equal("位置 200 ms / 700 ms", viewModel.DetailText);
        Assert.Equal("暂停", viewModel.PrimaryActionText);
    }

    [Fact]
    public async Task PlayDemoMp3Command_starts_demo_request()
    {
        var coordinator = new FakePlaybackCoordinator();
        var viewModel = new PlayerViewModel(coordinator, new FakePlaybackDemoRequestFactory());

        await viewModel.PlayDemoMp3Command.ExecuteAsync(null);

        Assert.NotNull(coordinator.LastStartedRequest);
        Assert.Equal("内置演示 MP3", coordinator.LastStartedRequest!.DisplayTitle);
    }

    [Fact]
    public async Task TogglePlayPauseCommand_pauses_current_playback()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
            "内置演示 WAV",
            "demo-book",
            0,
            0,
            0,
            700,
            null,
            false));

        var viewModel = new PlayerViewModel(coordinator, new FakePlaybackDemoRequestFactory());

        await viewModel.TogglePlayPauseCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.PauseCallCount);
    }

    [Fact]
    public void SnapshotChanged_updates_error_projection()
    {
        var coordinator = new FakePlaybackCoordinator();
        var viewModel = new PlayerViewModel(coordinator, new FakePlaybackDemoRequestFactory());

        coordinator.Publish(new PlaybackSnapshot(
            PlaybackState.Faulted,
            "损坏演示音频",
            "demo-book",
            0,
            2,
            0,
            0,
            "音频解码失败，请更换音频文件后重试。",
            false));

        Assert.True(viewModel.IsFaulted);
        Assert.Equal("播放失败", viewModel.StatusText);
        Assert.Equal("音频解码失败，请更换音频文件后重试。", viewModel.ErrorText);
        Assert.Equal("音频解码失败，请更换音频文件后重试。", viewModel.DetailText);
    }

    private sealed class FakePlaybackCoordinator : IPlaybackCoordinator
    {
        public FakePlaybackCoordinator()
            : this(PlaybackSnapshot.Idle)
        {
        }

        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; private set; }

        public PlaybackRequest? LastStartedRequest { get; private set; }

        public int PauseCallCount { get; private set; }

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public Task StartAsync(PlaybackRequest request, CancellationToken cancellationToken)
        {
            LastStartedRequest = request;
            Publish(new PlaybackSnapshot(
                PlaybackState.Playing,
                request.DisplayTitle,
                request.BookId,
                request.ChapterIndex,
                request.SegmentIndex,
                request.ResumePositionMilliseconds,
                800,
                null,
                request.IsUsingCache));
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with { State = PlaybackState.Playing });
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            PauseCallCount++;
            Publish(CurrentSnapshot with { State = PlaybackState.Paused });
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                State = PlaybackState.Stopped,
                PositionMilliseconds = 0,
                Message = "已停止本地音频播放。"
            });
            return Task.CompletedTask;
        }

        public Task SeekAsync(long positionMilliseconds, CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with { PositionMilliseconds = positionMilliseconds });
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class FakePlaybackDemoRequestFactory : IPlaybackDemoRequestFactory
    {
        public PlaybackRequest CreateWavDemoRequest() => new("demo.wav", "内置演示 WAV", "demo-book", 0, 0, 0, false);
        public PlaybackRequest CreateMp3DemoRequest() => new("demo.mp3", "内置演示 MP3", "demo-book", 0, 1, 0, false);
        public PlaybackRequest CreateCorruptDemoRequest() => new("broken.mp3", "损坏演示音频", "demo-book", 0, 2, 0, false);
    }
}
