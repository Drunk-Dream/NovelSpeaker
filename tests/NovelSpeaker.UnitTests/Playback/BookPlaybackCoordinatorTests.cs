using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed class BookPlaybackCoordinatorTests
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

        await WaitForAsync(() =>
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

        await WaitForAsync(() => coordinator.CurrentSnapshot.ChapterIndex == 1);
        Assert.Equal("第二章 延续", coordinator.CurrentSnapshot.ChapterTitle);

        localCoordinator.RaiseCompleted();

        await WaitForAsync(() => coordinator.CurrentSnapshot.State == PlaybackState.Stopped);
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
    public async Task SkipCurrentSegment_moves_to_following_segment_after_failure()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        audioProvider.EnqueueFailure(TtsErrorKind.ServerError, "服务错误。");
        audioProvider.EnqueueSuccess("audio-skip.mp3");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        Assert.Equal(PlaybackState.Faulted, coordinator.CurrentSnapshot.State);

        await coordinator.SkipCurrentSegmentAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
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
            [new PlaybackChapterContent(0, "第一章 开始", [new SpeechSegment(0, 0, 6, "新展示", "新语音")])]);

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
            [new PlaybackChapterContent(0, "第一章 开始", [new SpeechSegment(0, 0, 6, "新展示", "第一段")])]);

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
                [new PlaybackChapterContent(0, "第一章 开始",
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

    [Fact]
    public async Task StartAsync_without_explicit_position_restores_saved_progress()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry("book-1", 0, 1, 6, 333, "2026-06-25T00:00:00.0000000Z")
        };
        await using var coordinator = CreateCoordinator(localCoordinator, readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(333, localCoordinator.LastStartedRequest?.ResumePositionMilliseconds);
    }

    [Fact]
    public async Task StartAsync_with_explicit_position_ignores_saved_progress()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry("book-1", 0, 1, 6, 333, "2026-06-25T00:00:00.0000000Z")
        };
        await using var coordinator = CreateCoordinator(localCoordinator, readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 0, 0, null, 10), CancellationToken.None);

        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(0, localCoordinator.LastStartedRequest?.ResumePositionMilliseconds);
    }

    [Fact]
    public async Task StartAsync_remaps_saved_progress_by_character_offset_when_segment_index_is_missing()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry("book-1", 0, 8, 6, 333, "2026-06-25T00:00:00.0000000Z")
        };
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            readingProgressStore: readingProgressStore,
            book: CreateRemappedBook());

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(0, localCoordinator.LastStartedRequest?.ResumePositionMilliseconds);
    }

    [Fact]
    public async Task NextSegmentAsync_saves_previous_progress_before_switching_segments()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(localCoordinator, readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.SetPosition(240);

        await coordinator.NextSegmentAsync(CancellationToken.None);

        var saved = Assert.Single(readingProgressStore.SavedProgress);
        Assert.Equal(0, saved.SegmentIndex);
        Assert.Equal(0, saved.CharacterOffset);
        Assert.Equal(240, saved.AudioPositionMilliseconds);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task PreviousSegmentAsync_double_tap_while_playing_stops_intermediate_segment_before_buffering()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 0, 2, null, 10), CancellationToken.None);
        await coordinator.PreviousSegmentAsync(CancellationToken.None);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);

        var pendingAudio = audioProvider.EnqueuePendingSuccess("audio-delayed-previous.mp3");
        var secondPreviousTask = coordinator.PreviousSegmentAsync(CancellationToken.None);

        await WaitForAsync(() => audioProvider.Requests.Count == 3);
        Assert.False(localCoordinator.TryRaiseCompleted());

        pendingAudio.CompleteSuccess();
        await secondPreviousTask;

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task PreviousSegmentAsync_after_pausing_keeps_paused_context_without_rebuffering()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 0, 2, null, 10), CancellationToken.None);
        await coordinator.PauseAsync(CancellationToken.None);

        await coordinator.PreviousSegmentAsync(CancellationToken.None);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);

        var requestCountBeforeSecondJump = audioProvider.Requests.Count;
        await coordinator.PreviousSegmentAsync(CancellationToken.None);
        Assert.False(localCoordinator.TryRaiseCompleted());
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(requestCountBeforeSecondJump, audioProvider.Requests.Count);
    }

    [Fact]
    public async Task OpenPausedAsync_restores_saved_progress_without_requesting_audio_until_resume()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry("book-1", 0, 1, 6, 333, "2026-06-25T00:00:00.0000000Z")
        };
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            readingProgressStore: readingProgressStore);

        await coordinator.OpenPausedAsync(new OpenBookPlaybackRequest("book-1", null, null, 10), CancellationToken.None);

        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(333, coordinator.CurrentSnapshot.PositionMilliseconds);
        Assert.Empty(audioProvider.Requests);

        await coordinator.ResumeAsync(CancellationToken.None);

        Assert.Single(audioProvider.Requests);
        Assert.Equal(333, localCoordinator.LastStartedRequest?.ResumePositionMilliseconds);
    }

    [Fact]
    public async Task JumpToSegmentAsync_while_paused_updates_position_without_immediate_audio_request()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());

        await coordinator.OpenPausedAsync(new OpenBookPlaybackRequest("book-1", 0, 0, 10), CancellationToken.None);
        Assert.Empty(audioProvider.Requests);

        await coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);

        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Empty(audioProvider.Requests);
    }

    [Fact]
    public async Task JumpToSegmentAsync_while_playing_keeps_playing_and_requests_new_audio()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 0, 0, null, 10), CancellationToken.None);
        await coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, audioProvider.Requests.Count);
    }

    [Fact]
    public async Task StartAsync_can_surface_cached_audio_usage_in_snapshot()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        audioProvider.EnqueueCachedSuccess("audio-cached.mp3");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        Assert.True(coordinator.CurrentSnapshot.IsUsingCache);
        Assert.Equal("audio-cached.mp3", localCoordinator.LastStartedRequest?.FilePath);
    }

    [Fact]
    public async Task DisposeAsync_saves_current_progress_before_releasing_session()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore();
        var coordinator = CreateCoordinator(localCoordinator, readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.SetPosition(512);

        await coordinator.DisposeAsync();

        var saved = Assert.Single(readingProgressStore.SavedProgress);
        Assert.Equal(512, saved.AudioPositionMilliseconds);
        Assert.Equal(0, saved.CharacterOffset);
    }

    [Fact]
    public async Task StopAsync_cancels_active_prefetch_session()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var prefetchScheduler = new FakePrefetchScheduler();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            book: CreateThreeSegmentBook(),
            prefetchScheduler: prefetchScheduler);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        var sessionId = Assert.Single(prefetchScheduler.ScheduleCalls).SessionId;

        await coordinator.StopAsync(CancellationToken.None);

        Assert.Contains(sessionId, prefetchScheduler.CancelledSessions);
    }

    private static PlaybackCoordinator CreateCoordinator(
        FakeLocalAudioPlaybackCoordinator localCoordinator,
        FakeBookPlaybackContentService? bookContentService = null,
        FakeSelectedTtsRuleProvider? selectedRuleProvider = null,
        FakePlaybackAudioProvider? audioProvider = null,
        PlaybackBookContent? book = null,
        FakeReadingProgressStore? readingProgressStore = null,
        FakePrefetchScheduler? prefetchScheduler = null,
        FakeAppSettingsStore? appSettingsStore = null)
    {
        return new PlaybackCoordinator(
            bookContentService ?? new FakeBookPlaybackContentService(book ?? CreateBook()),
            selectedRuleProvider ?? new FakeSelectedTtsRuleProvider(CreateRuleSelection(1, "默认规则")),
            audioProvider ?? new FakePlaybackAudioProvider(),
            new AudioCacheProtectionRegistry(),
            localCoordinator,
            readingProgressStore ?? new FakeReadingProgressStore(),
            prefetchScheduler ?? new FakePrefetchScheduler(),
            appSettingsStore ?? new FakeAppSettingsStore(AppSettings.Default));
    }

    private static PlaybackBookContent CreateBook()
    {
        return new PlaybackBookContent(
            "book-1",
            "示例小说",
            [
                new PlaybackChapterContent(
                    0,
                    "第一章 开始",
                    [
                        new SpeechSegment(0, 0, 6, "第一段", "第一段"),
                        new SpeechSegment(1, 6, 6, "第二段", "第二段")
                    ])
            ]);
    }

    private static PlaybackBookContent CreateTwoChapterBook()
    {
        return new PlaybackBookContent(
            "book-1",
            "示例小说",
            [
                new PlaybackChapterContent(
                    0,
                    "第一章 开始",
                    [new SpeechSegment(0, 0, 6, "第一段", "第一段")]),
                new PlaybackChapterContent(
                    1,
                    "第二章 延续",
                    [new SpeechSegment(0, 6, 6, "第二章 第一段", "第二章 第一段")])
            ]);
    }

    private static PlaybackBookContent CreateThreeSegmentBook()
    {
        return new PlaybackBookContent(
            "book-1",
            "示例小说",
            [
                new PlaybackChapterContent(
                    0,
                    "第一章 开始",
                    [
                        new SpeechSegment(0, 0, 6, "第一段", "第一段"),
                        new SpeechSegment(1, 6, 6, "第二段", "第二段"),
                        new SpeechSegment(2, 12, 6, "第三段", "第三段")
                    ])
            ]);
    }

    private static PlaybackBookContent CreateRemappedBook()
    {
        return new PlaybackBookContent(
            "book-1",
            "示例小说",
            [
                new PlaybackChapterContent(
                    0,
                    "第一章 开始",
                    [
                        new SpeechSegment(0, 0, 3, "甲段", "甲段"),
                        new SpeechSegment(1, 6, 3, "乙段", "乙段")
                    ])
            ]);
    }

    private static SelectedPlaybackRule CreateRuleSelection(long id, string name)
    {
        var rule = new HttpTtsRule(
            id,
            name,
            "https://example.com/tts?text={{encodeURIComponent(speakText)}}&speed={{speakSpeed}}",
            "audio/mpeg",
            null,
            null,
            null,
            null,
            true,
            null,
            "2026-06-24T00:00:00.0000000Z",
            "2026-06-24T00:00:00.0000000Z");

        return new SelectedPlaybackRule(id, name, rule, rule.ToNormalizedRule());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out while waiting for the playback coordinator to update.");
    }

    private sealed class FakeBookPlaybackContentService : IBookPlaybackContentService
    {
        public FakeBookPlaybackContentService(PlaybackBookContent book)
        {
            Book = book;
        }

        public PlaybackBookContent Book { get; set; }

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            if (bookId != Book.BookId)
            {
                return Task.FromResult<PlaybackBookContent?>(null);
            }

            var metadataOnly = new PlaybackBookContent(
                Book.BookId,
                Book.BookTitle,
                Book.Chapters
                    .Select(chapter => new PlaybackChapterContent(chapter.ChapterIndex, chapter.Title, []))
                    .ToArray(),
                Book.BookAuthor);
            return Task.FromResult<PlaybackBookContent?>(metadataOnly);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            if (bookId != Book.BookId)
            {
                return Task.FromResult<PlaybackChapterContent?>(null);
            }

            return Task.FromResult<PlaybackChapterContent?>(Book.Chapters.FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex));
        }
    }

    private sealed class FakeSelectedTtsRuleProvider : ISelectedTtsRuleProvider
    {
        private readonly Dictionary<long, SelectedPlaybackRule> _rules = [];

        public FakeSelectedTtsRuleProvider(SelectedPlaybackRule? selectedRule)
        {
            if (selectedRule is not null)
            {
                SelectedRule = selectedRule;
                _rules[selectedRule.RuleId] = selectedRule;
            }
        }

        public SelectedPlaybackRule? SelectedRule { get; private set; }

        public void RegisterSelectable(SelectedPlaybackRule rule)
        {
            _rules[rule.RuleId] = rule;
        }

        public Task<SelectedPlaybackRule?> GetSelectedRuleAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SelectedRule);
        }

        public Task<SelectedPlaybackRule?> SelectRuleAsync(long ruleId, CancellationToken cancellationToken)
        {
            SelectedRule = _rules.GetValueOrDefault(ruleId);
            return Task.FromResult(SelectedRule);
        }
    }

    private sealed class FakePlaybackAudioProvider : IPlaybackAudioProvider
    {
        private readonly Queue<Func<Task<PlaybackAudioResult>>> _results = [];

        public List<PlaybackAudioRequest> Requests { get; } = [];

        public int InvalidateCallCount { get; private set; }

        public void EnqueueFailure(TtsErrorKind kind, string message)
        {
            _results.Enqueue(() => Task.FromResult(new PlaybackAudioResult(
                null,
                false,
                new TtsExecutionFailure(kind, message, null, null, null, null))));
        }

        public void EnqueueSuccess(string filePath)
        {
            _results.Enqueue(() => Task.FromResult(new PlaybackAudioResult(filePath, false, null)));
        }

        public void EnqueueCachedSuccess(string filePath)
        {
            _results.Enqueue(() => Task.FromResult(new PlaybackAudioResult(filePath, true, null)));
        }

        public PendingAudioResult EnqueuePendingSuccess(string filePath)
        {
            var completionSource = new TaskCompletionSource<PlaybackAudioResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _results.Enqueue(() => completionSource.Task);
            return new PendingAudioResult(completionSource, filePath);
        }

        public Task<PlaybackAudioResult> GetAudioAsync(
            PlaybackAudioRequest request,
            PlaybackAudioPriority priority,
            Action<PlaybackAudioProgress>? progressCallback,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_results.Count > 0)
            {
                return _results.Dequeue().Invoke();
            }

            return Task.FromResult(new PlaybackAudioResult($"audio-{Requests.Count}.mp3", false, null));
        }

        public Task InvalidateAsync(PlaybackAudioRequest request, CancellationToken cancellationToken)
        {
            InvalidateCallCount++;
            return Task.CompletedTask;
        }

        public sealed class PendingAudioResult
        {
            private readonly TaskCompletionSource<PlaybackAudioResult> _completionSource;
            private readonly string _filePath;

            public PendingAudioResult(TaskCompletionSource<PlaybackAudioResult> completionSource, string filePath)
            {
                _completionSource = completionSource;
                _filePath = filePath;
            }

            public void CompleteSuccess()
            {
                _completionSource.TrySetResult(new PlaybackAudioResult(_filePath, false, null));
            }
        }
    }

    private sealed class FakeLocalAudioPlaybackCoordinator : ILocalAudioPlaybackCoordinator
    {
        public LocalAudioPlaybackSnapshot CurrentSnapshot { get; private set; } = LocalAudioPlaybackSnapshot.Idle;

        public LocalAudioPlaybackRequest? LastStartedRequest { get; private set; }

        public int StopCallCount { get; private set; }

        public event EventHandler<LocalAudioPlaybackSnapshot>? SnapshotChanged;

        public event EventHandler? PlaybackCompleted;

        public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

        public Task StartAsync(LocalAudioPlaybackRequest request, CancellationToken cancellationToken)
        {
            LastStartedRequest = request;
            CurrentSnapshot = new LocalAudioPlaybackSnapshot(
                PlaybackState.Playing,
                request.DisplayTitle,
                request.BookId,
                request.ChapterIndex,
                request.SegmentIndex,
                request.ResumePositionMilliseconds,
                1800,
                null,
                request.IsUsingCache);
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken)
        {
            CurrentSnapshot = CurrentSnapshot with { State = PlaybackState.Playing };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            CurrentSnapshot = CurrentSnapshot with { State = PlaybackState.Paused };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCallCount++;
            CurrentSnapshot = CurrentSnapshot with
            {
                State = PlaybackState.Stopped,
                PositionMilliseconds = 0
            };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public Task SeekAsync(long positionMilliseconds, CancellationToken cancellationToken)
        {
            CurrentSnapshot = CurrentSnapshot with { PositionMilliseconds = positionMilliseconds };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void RaiseCompleted()
        {
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }

        public bool TryRaiseCompleted()
        {
            if (CurrentSnapshot.State != PlaybackState.Playing)
            {
                return false;
            }

            RaiseCompleted();
            return true;
        }

        public void RaiseFailed(PlaybackErrorKind kind, string message)
        {
            PlaybackFailed?.Invoke(this, new PlaybackErrorEventArgs(kind, message));
        }

        public void SetPosition(long positionMilliseconds)
        {
            CurrentSnapshot = CurrentSnapshot with { PositionMilliseconds = positionMilliseconds };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
        }
    }

    private sealed class FakeReadingProgressStore : IReadingProgressStore
    {
        public List<PlaybackProgressUpdate> SavedProgress { get; } = [];

        public ReadingProgressEntry? StoredProgress { get; set; }

        public Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken)
        {
            SavedProgress.Add(progress);
            StoredProgress = new ReadingProgressEntry(
                progress.BookId,
                progress.ChapterIndex,
                progress.SegmentIndex,
                progress.CharacterOffset,
                progress.AudioPositionMilliseconds,
                DateTime.UtcNow.ToString("O"));
            return Task.CompletedTask;
        }

        public Task<ReadingProgressEntry?> GetAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                StoredProgress is not null && string.Equals(StoredProgress.BookId, bookId, StringComparison.Ordinal)
                    ? StoredProgress
                    : null);
        }

        public Task<ReadingProgressEntry?> GetMostRecentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(StoredProgress);
        }
    }

    private sealed class FakePrefetchScheduler : IPrefetchScheduler
    {
        public List<(Guid SessionId, IReadOnlyList<PlaybackAudioRequest> Requests)> ScheduleCalls { get; } = [];

        public List<Guid> CancelledSessions { get; } = [];

        public Task ScheduleAsync(Guid sessionId, IReadOnlyList<PlaybackAudioRequest> requests, CancellationToken cancellationToken)
        {
            ScheduleCalls.Add((sessionId, requests.ToArray()));
            return Task.CompletedTask;
        }

        public Task CancelAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            CancelledSessions.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAppSettingsStore : IAppSettingsStore
    {
        public FakeAppSettingsStore(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Settings);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }
}
