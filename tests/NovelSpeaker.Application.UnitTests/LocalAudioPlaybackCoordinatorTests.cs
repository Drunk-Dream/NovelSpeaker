using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class LocalAudioPlaybackCoordinatorTests
{
    [Fact]
    public void CurrentSnapshot_is_idle_by_default()
    {
        var coordinator = new LocalAudioPlaybackCoordinator(new FakeAudioPlayer());

        Assert.Equal(PlaybackState.Idle, coordinator.CurrentSnapshot.State);
        Assert.Equal("准备播放本地音频。", coordinator.CurrentSnapshot.Message);
    }

    [Fact]
    public async Task StartAsync_publishes_playing_snapshot_with_duration_and_metadata()
    {
        var player = new FakeAudioPlayer();
        player.SetDuration(TimeSpan.FromMilliseconds(2400));
        await using var coordinator = new LocalAudioPlaybackCoordinator(player);

        await coordinator.StartAsync(CreateRequest("内置演示 WAV", 300), CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal("内置演示 WAV", coordinator.CurrentSnapshot.DisplayTitle);
        Assert.Equal("demo-book", coordinator.CurrentSnapshot.BookId);
        Assert.Equal(0, coordinator.CurrentSnapshot.ChapterIndex);
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(300, coordinator.CurrentSnapshot.PositionMilliseconds);
        Assert.Equal(2400, coordinator.CurrentSnapshot.DurationMilliseconds);
    }

    [Fact]
    public async Task Pause_resume_and_stop_update_snapshot_from_player_position()
    {
        var player = new FakeAudioPlayer();
        player.SetDuration(TimeSpan.FromMilliseconds(2000));
        await using var coordinator = new LocalAudioPlaybackCoordinator(player);

        await coordinator.StartAsync(CreateRequest("内置演示 WAV"), CancellationToken.None);
        player.SetPosition(TimeSpan.FromMilliseconds(750));

        await coordinator.PauseAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(750, coordinator.CurrentSnapshot.PositionMilliseconds);

        await coordinator.ResumeAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);

        await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
        Assert.Equal(0, coordinator.CurrentSnapshot.PositionMilliseconds);
    }

    [Fact]
    public async Task SeekAsync_clamps_to_audio_duration()
    {
        var player = new FakeAudioPlayer();
        player.SetDuration(TimeSpan.FromMilliseconds(900));
        await using var coordinator = new LocalAudioPlaybackCoordinator(player);

        await coordinator.StartAsync(CreateRequest("内置演示 WAV"), CancellationToken.None);
        await coordinator.SeekAsync(1200, CancellationToken.None);

        Assert.Equal(900, coordinator.CurrentSnapshot.PositionMilliseconds);
    }

    [Fact]
    public async Task SetVolume_clamps_and_applies_application_volume_to_audio_player()
    {
        var player = new FakeAudioPlayer();
        await using var coordinator = new LocalAudioPlaybackCoordinator(player);

        coordinator.SetVolume(0.35);

        Assert.Equal(0.35, coordinator.Volume);
        Assert.Equal(0.35, player.Volume);
        Assert.Equal(0.35, coordinator.CurrentSnapshot.Volume);

        coordinator.SetVolume(2);
        Assert.Equal(1, coordinator.Volume);
        Assert.Equal(1, player.Volume);

        coordinator.SetVolume(-1);
        Assert.Equal(0, coordinator.Volume);
        Assert.Equal(0, player.Volume);
    }

    [Fact]
    public async Task StartAsync_ignores_stale_completion_from_previous_subscription()
    {
        var player = new FakeAudioPlayer();
        await using var coordinator = new LocalAudioPlaybackCoordinator(player);

        await coordinator.StartAsync(CreateRequest("第一次请求"), CancellationToken.None);
        var firstSubscriptionIndex = player.CompletedSubscriptionCount - 1;

        await coordinator.StartAsync(CreateRequest("第二次请求", segmentIndex: 1), CancellationToken.None);
        player.RaiseHistoricalCompleted(firstSubscriptionIndex);

        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.DisplayTitle == "第二次请求");
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal("第二次请求", coordinator.CurrentSnapshot.DisplayTitle);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task Playback_failed_event_moves_snapshot_to_faulted()
    {
        var player = new FakeAudioPlayer();
        await using var coordinator = new LocalAudioPlaybackCoordinator(player);

        await coordinator.StartAsync(CreateRequest("损坏音频"), CancellationToken.None);
        player.SetPosition(TimeSpan.FromMilliseconds(120));
        player.RaiseFailed(PlaybackErrorKind.AudioDecode, "音频解码失败，请更换音频文件后重试。");

        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.State == PlaybackState.Faulted);
        Assert.Equal("损坏音频", coordinator.CurrentSnapshot.DisplayTitle);
        Assert.Equal(120, coordinator.CurrentSnapshot.PositionMilliseconds);
        Assert.Equal("音频解码失败，请更换音频文件后重试。", coordinator.CurrentSnapshot.Message);
    }

    private static LocalAudioPlaybackRequest CreateRequest(
        string title,
        long resumePositionMilliseconds = 0,
        int segmentIndex = 0)
    {
        return new LocalAudioPlaybackRequest(
            "demo.wav",
            title,
            "demo-book",
            0,
            segmentIndex,
            resumePositionMilliseconds,
            false);
    }

    private static async Task WaitForAsync(
        LocalAudioPlaybackCoordinator coordinator,
        Func<bool> condition)
    {
        if (condition())
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<LocalAudioPlaybackSnapshot>? handler = null;
        handler = (_, _) =>
        {
            if (condition())
            {
                completion.TrySetResult();
            }
        };
        coordinator.SnapshotChanged += handler;
        try
        {
            if (condition())
            {
                return;
            }

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            coordinator.SnapshotChanged -= handler;
        }
    }
}
