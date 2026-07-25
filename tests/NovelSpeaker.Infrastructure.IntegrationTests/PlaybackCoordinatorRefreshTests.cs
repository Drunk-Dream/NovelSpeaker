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
    public async Task RetryCurrentSegment_replays_failed_segment()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        audioProvider.EnqueueFailure(TtsErrorKind.Network, "网络失败。");
        audioProvider.EnqueueSuccess("audio-retry.mp3");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        Assert.Equal(PlaybackState.Faulted, coordinator.CurrentSnapshot.State);

        await coordinator.RetryCurrentSegmentAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal("audio-retry.mp3", localCoordinator.LastStartedRequest?.FilePath);
        Assert.Equal(2, audioProvider.Requests.Count);
    }

    [Fact]
    public async Task Repeated_current_segment_failures_reach_the_recovery_pause_threshold()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        audioProvider.EnqueueFailure(TtsErrorKind.ServerError, "服务错误。 ");
        audioProvider.EnqueueFailure(TtsErrorKind.ServerError, "服务错误。 ");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        Assert.Equal(PlaybackState.Faulted, coordinator.CurrentSnapshot.State);

        await coordinator.RetryCurrentSegmentAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Faulted, coordinator.CurrentSnapshot.State);
        Assert.Contains("连续 2 段", coordinator.CurrentSnapshot.Message, StringComparison.Ordinal);
        Assert.True(coordinator.CurrentSnapshot.CanRetry);
    }

    [Fact]
    public async Task StartAsync_without_selected_rule_preserves_context_without_entering_playback_fault()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            selectedRuleProvider: new FakeSelectedTtsRuleProvider(null));

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
        Assert.Contains("TTS 规则", coordinator.CurrentSnapshot.Message);
        Assert.False(coordinator.CurrentSnapshot.CanRetry);
        Assert.False(coordinator.CurrentSnapshot.HasAvailableRule);
        Assert.Equal("book-1", coordinator.CurrentSnapshot.BookId);
    }

    [Fact]
    public async Task AudioDecode_failure_invalidates_and_regenerates_current_segment_once()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.RaiseFailed(PlaybackErrorKind.AudioDecode, "音频损坏。");

        await WaitForAsync(() => audioProvider.InvalidateCallCount == 1);
        await WaitForAsync(() => audioProvider.Requests.Count == 2);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
    }

    [Fact]
    public async Task ChangeRule_and_change_speed_restart_current_segment()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var selectedRuleProvider = new FakeSelectedTtsRuleProvider(CreateRuleSelection(1, "默认规则"));
        selectedRuleProvider.RegisterSelectable(CreateRuleSelection(2, "备用规则"));
        var prefetchScheduler = new FakePrefetchScheduler();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            selectedRuleProvider: selectedRuleProvider,
            prefetchScheduler: prefetchScheduler);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        await coordinator.ChangeRuleAsync(2, CancellationToken.None);

        Assert.Equal("备用规则", coordinator.CurrentSnapshot.RuleName);
        Assert.NotEmpty(prefetchScheduler.CancelledSessions);

        await coordinator.ChangeSpeedAsync(16, CancellationToken.None);

        Assert.Equal(16, coordinator.CurrentSnapshot.SpeakSpeed);
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task RefreshBookMetadataAsync_updates_active_snapshot_without_changing_playback_state()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var bookContentService = new FakeBookPlaybackContentService(CreateBook());
        await using var coordinator = CreateCoordinator(localCoordinator, bookContentService: bookContentService);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        bookContentService.Book = new PlaybackBookContent(
            "book-1",
            "已更新书名",
            CreateBook().Chapters,
            "已更新作者");

        await coordinator.RefreshBookMetadataAsync("book-1", CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal("已更新书名", coordinator.CurrentSnapshot.BookTitle);
        Assert.Equal("已更新作者", coordinator.CurrentSnapshot.BookAuthor);
    }

    [Fact]
    public async Task RefreshRegexReplacementAsync_restarts_playback_when_mapped_speech_changes()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        var content = new FakeBookPlaybackContentService(CreateBook());
        await using var coordinator = CreateCoordinator(localCoordinator, bookContentService: content, audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        content.Book = new PlaybackBookContent(
            "book-1",
            "示例小说",
            [PlaybackChapterContent.FromLoaded(0, "第一章 开始", [new SpeechSegment(0, 0, 6, "新展示", "新语音")])]);

        await coordinator.RefreshRegexReplacementAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, audioProvider.Requests.Count);
        Assert.Equal("新语音", audioProvider.Requests[1].SpeechText);
        Assert.Equal(1, coordinator.CurrentSnapshot.ContentRevision);
    }

    [Fact]
    public async Task RefreshRegexReplacementAsync_keeps_current_audio_when_only_display_changes()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        var content = new FakeBookPlaybackContentService(CreateBook());
        await using var coordinator = CreateCoordinator(localCoordinator, bookContentService: content, audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        content.Book = new PlaybackBookContent(
            "book-1",
            "示例小说",
            [PlaybackChapterContent.FromLoaded(0, "第一章 开始", [new SpeechSegment(0, 0, 6, "新展示", "第一段")])]);

        await coordinator.RefreshRegexReplacementAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Single(audioProvider.Requests);
        Assert.Equal(1, coordinator.CurrentSnapshot.ContentRevision);
    }

    [Fact]
    public async Task StartAsync_skips_consecutive_empty_speech_segments_without_requesting_tts()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: new PlaybackBookContent(
                "book-1",
                "示例小说",
                [PlaybackChapterContent.FromLoaded(0, "第一章 开始",
                [
                    new SpeechSegment(0, 0, 2, "仅展示一", string.Empty),
                    new SpeechSegment(1, 3, 2, "仅展示二", string.Empty),
                    new SpeechSegment(2, 6, 2, "可朗读", "可朗读")
                ])]));

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        var request = Assert.Single(audioProvider.Requests);
        Assert.Equal(2, request.SegmentIndex);
        Assert.Equal("可朗读", request.SpeechText);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task StartAsync_loads_regex_filtered_empty_chapter_once_and_advances_stably()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var content = new FakeBookPlaybackContentService(new PlaybackBookContent(
            "book-1",
            "示例小说",
            [
                PlaybackChapterContent.FromLoaded(0, "第一章 被过滤", []),
                PlaybackChapterContent.FromLoaded(
                    1,
                    "第二章 可播放",
                    [new SpeechSegment(0, 8, 4, "第二章", "第二章")])
            ]));
        await using var coordinator = CreateCoordinator(localCoordinator, bookContentService: content);

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, null, null, 10),
            CancellationToken.None);

        Assert.Equal(1, coordinator.CurrentSnapshot.ChapterIndex);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, content.GetChapterCallCounts[0]);

        localCoordinator.RaiseCompleted();
        await WaitForAsync(() => coordinator.CurrentSnapshot.State == PlaybackState.Stopped);

        Assert.Equal("全书播放完成。", coordinator.CurrentSnapshot.Message);
        Assert.Equal(1, content.GetChapterCallCounts[0]);
    }

    [Fact]
    public async Task HandleBookDeletedAsync_stops_current_session_and_publishes_idle()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var prefetchScheduler = new FakePrefetchScheduler();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            prefetchScheduler: prefetchScheduler);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        await coordinator.HandleBookDeletedAsync("book-1", CancellationToken.None);

        Assert.Equal(PlaybackState.Idle, coordinator.CurrentSnapshot.State);
        Assert.Null(coordinator.CurrentSnapshot.BookId);
        Assert.Equal(1, localCoordinator.StopCallCount);
        Assert.NotEmpty(prefetchScheduler.CancelledSessions);
    }

    [Fact]
    public async Task RefreshBookMetadataAsync_and_HandleBookDeletedAsync_ignore_other_books()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(localCoordinator);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        var snapshotBefore = coordinator.CurrentSnapshot;

        await coordinator.RefreshBookMetadataAsync("book-2", CancellationToken.None);
        await coordinator.HandleBookDeletedAsync("book-2", CancellationToken.None);

        Assert.Equal(snapshotBefore, coordinator.CurrentSnapshot);
    }

}
