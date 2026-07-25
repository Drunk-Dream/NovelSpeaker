using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.UnitTests.Speech;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed partial class PlaybackCoordinatorTests
{
    [Fact]
    public async Task StartAsync_with_selected_rule_and_audio_result_enters_playing_state()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 12), CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal("示例小说", coordinator.CurrentSnapshot.BookTitle);
        Assert.Equal("第一章 开始", coordinator.CurrentSnapshot.ChapterTitle);
        Assert.Equal(12, coordinator.CurrentSnapshot.SpeakSpeed);
        Assert.Equal("默认规则", coordinator.CurrentSnapshot.RuleName);
        Assert.Equal("audio-1.mp3", localCoordinator.LastStartedRequest?.FilePath);
        Assert.Single(audioProvider.Requests);
    }

    [Fact]
    public async Task PlaybackCompleted_advances_to_next_segment()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(localCoordinator);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.RaiseCompleted();

        await WaitForAsync(coordinator, () =>
            coordinator.CurrentSnapshot.SegmentIndex == 1 &&
            coordinator.CurrentSnapshot.State == PlaybackState.Playing);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task PlaybackCompleted_moves_to_next_chapter_and_stops_at_book_end()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            book: CreateTwoChapterBook());

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.RaiseCompleted();

        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.ChapterIndex == 1);
        Assert.Equal("第二章 延续", coordinator.CurrentSnapshot.ChapterTitle);

        localCoordinator.RaiseCompleted();

        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.State == PlaybackState.Stopped);
        Assert.Equal("全书播放完成。", coordinator.CurrentSnapshot.Message);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task StartAsync_uses_settings_prefetch_count(int configuredPrefetchCount, int expectedRequests)
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var prefetchScheduler = new FakePrefetchScheduler();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            book: CreateThreeSegmentBook(),
            prefetchScheduler: prefetchScheduler,
            appSettingsStore: new FakeAppSettingsStore(AppSettings.Default with { PrefetchCount = configuredPrefetchCount }));

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        var scheduleCall = Assert.Single(prefetchScheduler.ScheduleCalls);
        Assert.Equal(expectedRequests, scheduleCall.Requests.Count);
    }

    [Fact]
    public async Task StartAsync_prefetches_across_chapter_boundary()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var prefetchScheduler = new FakePrefetchScheduler();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            book: CreateTwoChapterBook(),
            prefetchScheduler: prefetchScheduler,
            appSettingsStore: new FakeAppSettingsStore(AppSettings.Default with { PrefetchCount = 1 }));

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        var request = Assert.Single(Assert.Single(prefetchScheduler.ScheduleCalls).Requests);
        Assert.Equal(1, request.ChapterIndex);
        Assert.Equal(0, request.SegmentIndex);
    }

    [Fact]
    public async Task Pause_and_resume_keep_current_session()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(localCoordinator, readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.SetPosition(420);

        await coordinator.PauseAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(420, coordinator.CurrentSnapshot.PositionMilliseconds);
        Assert.Equal(420, Assert.Single(readingProgressStore.SavedProgress).AudioPositionMilliseconds);

        await coordinator.ResumeAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
    }

    [Fact]
    public async Task PauseAsync_propagates_progress_save_failure()
    {
        var expected = new InvalidOperationException("保存失败");
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore { SaveFailure = expected };
        var coordinator = CreateCoordinator(
            localCoordinator,
            readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.PauseAsync(CancellationToken.None));

        Assert.Same(expected, actual);
        readingProgressStore.SaveFailure = null;
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task PauseAsync_reduces_prefetch_window_to_one_segment()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var prefetchScheduler = new FakePrefetchScheduler();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            book: CreateThreeSegmentBook(),
            prefetchScheduler: prefetchScheduler,
            appSettingsStore: new FakeAppSettingsStore(AppSettings.Default with { PrefetchCount = 2 }));

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        await coordinator.PauseAsync(CancellationToken.None);

        Assert.Equal(2, prefetchScheduler.ScheduleCalls.Count);
        Assert.Equal(2, prefetchScheduler.ScheduleCalls[0].Requests.Count);
        Assert.Single(prefetchScheduler.ScheduleCalls[1].Requests);
    }

}
