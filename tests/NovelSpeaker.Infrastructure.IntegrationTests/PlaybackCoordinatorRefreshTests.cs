using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.Library;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed partial class PlaybackCoordinatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Speed_change_keeps_current_audio_and_next_sentence_uses_new_speed(int speed)
    {
        var local = new FakeLocalAudioPlaybackCoordinator();
        var audio = new FakeAudioGenerationProvider();
        await using var coordinator = CreateCoordinator(local, audioProvider: audio);
        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 50), CancellationToken.None);
        var currentAudio = local.LastStartedRequest;
        await coordinator.ChangeSpeedAsync(speed, CancellationToken.None);
        Assert.Same(currentAudio, local.LastStartedRequest);
        Assert.Single(audio.Requests);
        Assert.Equal(speed, coordinator.CurrentSnapshot.SpeakSpeed);
        local.RaiseCompleted();
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SegmentIndex == 1 && coordinator.CurrentSnapshot.State == PlaybackState.Playing);
        Assert.Equal(speed, audio.Requests.Last().SpeakSpeed);
    }

    [Fact]
    public async Task Empty_audio_response_skips_one_segment_and_continues_playback()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        var readingProgressStore = new FakeReadingProgressStore();
        audioProvider.EnqueueFailure(TtsErrorKind.EmptyAudioResponse, "服务返回了空响应，无法生成音频。");
        audioProvider.EnqueueSuccess("second-segment.mp3");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            readingProgressStore: readingProgressStore,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 0, null, 10),
            CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal([0, 1], audioProvider.Requests.Select(request => request.SegmentIndex));
        Assert.Contains(readingProgressStore.SavedProgress, progress => progress.SegmentIndex == 1);
    }

    [Fact]
    public async Task Empty_audio_response_on_the_last_segment_ends_playback_cleanly()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        audioProvider.EnqueueFailure(TtsErrorKind.EmptyAudioResponse, "服务返回了空响应，无法生成音频。");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 2, null, 10),
            CancellationToken.None);

        Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
        Assert.Contains("已跳过并结束播放", coordinator.CurrentSnapshot.Message, StringComparison.Ordinal);
        Assert.Single(audioProvider.Requests);
    }

    [Fact]
    public async Task Three_final_failures_skip_three_segments_then_pause_before_requesting_the_fourth()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        audioProvider.EnqueueFailure(TtsErrorKind.EmptyAudioResponse, "第一个空响应。");
        audioProvider.EnqueueFailure(TtsErrorKind.Unauthorized, "认证失败。");
        audioProvider.EnqueueFailure(TtsErrorKind.Network, "网络失败。");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateSegmentBook(5));

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 0, null, 10),
            CancellationToken.None);

        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(3, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Contains("连续跳过 3", coordinator.CurrentSnapshot.Message, StringComparison.Ordinal);
        Assert.Equal([0, 1, 2], audioProvider.Requests.Select(request => request.SegmentIndex));

        await coordinator.ResumeAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(3, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal([0, 1, 2, 3], audioProvider.Requests.Select(request => request.SegmentIndex));
    }

    [Fact]
    public async Task Explicit_retry_after_recovery_pause_starts_a_new_failure_window()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        audioProvider.EnqueueFailure(TtsErrorKind.Network, "网络失败。");
        audioProvider.EnqueueFailure(TtsErrorKind.Network, "网络失败。");
        audioProvider.EnqueueFailure(TtsErrorKind.Network, "网络失败。");
        audioProvider.EnqueueSuccess("audio-retry.mp3");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateSegmentBook(5));

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);

        await coordinator.RetryCurrentSegmentAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal("audio-retry.mp3", localCoordinator.LastStartedRequest?.FilePath);
        Assert.Equal(3, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(4, audioProvider.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_successful_segment_resets_the_consecutive_failure_count(bool completeDuringStart)
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator
        {
            CompleteOnStartCall = completeDuringStart ? 1 : null
        };
        var audioProvider = new FakeAudioGenerationProvider();
        audioProvider.EnqueueFailure(TtsErrorKind.ServerError, "服务错误。");
        audioProvider.EnqueueFailure(TtsErrorKind.ServerError, "服务错误。");
        audioProvider.EnqueueSuccess("middle.mp3");
        audioProvider.EnqueueFailure(TtsErrorKind.ServerError, "服务错误。");
        audioProvider.EnqueueFailure(TtsErrorKind.ServerError, "服务错误。");
        audioProvider.EnqueueSuccess("last.mp3");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateSegmentBook(6));

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        if (!completeDuringStart)
        {
            Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
            Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
            localCoordinator.RaiseCompleted();
        }

        await WaitForAsync(coordinator, () =>
            coordinator.CurrentSnapshot.State == PlaybackState.Playing &&
            coordinator.CurrentSnapshot.SegmentIndex == 5);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal([0, 1, 2, 3, 4, 5], audioProvider.Requests.Select(request => request.SegmentIndex));
    }

    [Fact]
    public async Task StartAsync_without_selected_rule_preserves_context_without_entering_playback_fault()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            selectedProviderProvider: new FakeCurrentSpeechProvider(null));

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
        Assert.Contains("语音服务", coordinator.CurrentSnapshot.Message);
        Assert.False(coordinator.CurrentSnapshot.CanRetry);
        Assert.False(coordinator.CurrentSnapshot.HasAvailableProvider);
        Assert.Equal("book-1", coordinator.CurrentSnapshot.BookId);
    }

    [Fact]
    public async Task AudioDecode_failure_invalidates_and_regenerates_current_segment_once()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.RaiseFailed(PlaybackErrorKind.AudioDecode, "音频损坏。");

        await WaitForAsync(audioProvider, () => audioProvider.InvalidateCallCount == 1);
        await WaitForAsync(audioProvider, () => audioProvider.Requests.Count == 2);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
    }

    [Fact]
    public async Task Provider_and_speed_changes_preserve_current_sentence()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var selectedProviderProvider = new FakeCurrentSpeechProvider(CreateRuleSelection(1, "默认规则"));
        selectedProviderProvider.RegisterSelectable(CreateRuleSelection(2, "备用规则"));
        var prefetchScheduler = new FakePrefetchScheduler();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            selectedProviderProvider: selectedProviderProvider,
            prefetchScheduler: prefetchScheduler);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        await coordinator.ChangeProviderAsync(TestSpeechProviders.Id(2), CancellationToken.None);

        Assert.Equal("默认规则", coordinator.CurrentSnapshot.ProviderName);
        Assert.Equal(0, localCoordinator.StopCallCount);

        await coordinator.ChangeSpeedAsync(16, CancellationToken.None);

        Assert.Equal(16, coordinator.CurrentSnapshot.SpeakSpeed);
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Metadata_committed_change_updates_active_snapshot_without_changing_playback_state(bool stopBeforeMutation)
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var bookContentService = new FakeBookPlaybackContentService(CreateBook());
        var changes = new BookSourceChanges();
        changes.Changed += (_, _) => throw new InvalidOperationException("observer failure");
        var metadata = new BookMetadataUpdateService(new PlaybackMetadataStore(bookContentService), new BookMutationGate(), changes);
        await using var coordinator = CreateCoordinator(localCoordinator, bookContentService: bookContentService, sourceChanges: changes);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        if (stopBeforeMutation) await coordinator.StopAsync(CancellationToken.None);
        await metadata.UpdateMetadataAsync(new("book-1", "已更新书名", "已更新作者"), CancellationToken.None);
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.BookTitle == "已更新书名");

        Assert.Equal(stopBeforeMutation ? PlaybackState.Stopped : PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal("已更新书名", coordinator.CurrentSnapshot.BookTitle);
        Assert.Equal("已更新作者", coordinator.CurrentSnapshot.BookAuthor);
        Assert.Equal(1, localCoordinator.StartCallCount);
        Assert.Equal(stopBeforeMutation ? 1 : 0, localCoordinator.StopCallCount);
    }

    private sealed class PlaybackMetadataStore(FakeBookPlaybackContentService content) : IBookMetadataStore
    {
        public Task<BookDetailsHeader> UpdateAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
        {
            content.Book = content.Book! with { BookTitle = request.Title, BookAuthor = request.Author };
            return Task.FromResult(new BookDetailsHeader(request.BookId, request.Title, request.Author));
        }
    }

    [Fact]
    public async Task RefreshRegexReplacementAsync_restarts_playback_when_mapped_speech_changes()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
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
        var audioProvider = new FakeAudioGenerationProvider();
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
        var audioProvider = new FakeAudioGenerationProvider();
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
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.State == PlaybackState.Stopped);

        Assert.Equal("全书播放完成。", coordinator.CurrentSnapshot.Message);
        Assert.Equal(1, content.GetChapterCallCounts[0]);
    }

    [Fact]
    public async Task Removal_stops_once_and_committed_fact_does_not_stop_again()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var prefetchScheduler = new FakePrefetchScheduler();
        var changes = new SourceChanges();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            prefetchScheduler: prefetchScheduler, sourceChanges: changes);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        await coordinator.StopForRemovalAsync("book-1", null, CancellationToken.None);
        changes.Publish(new BookCommittedChange.BookRemoved("book-1"));
        await coordinator.PauseAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Idle, coordinator.CurrentSnapshot.State);
        Assert.Null(coordinator.CurrentSnapshot.BookId);
        Assert.Equal(1, localCoordinator.StopCallCount);
        Assert.NotEmpty(prefetchScheduler.CancelledSessions);
    }

    [Fact]
    public async Task Committed_changes_ignore_other_books()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var changes = new SourceChanges();
        await using var coordinator = CreateCoordinator(localCoordinator, sourceChanges: changes);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        var snapshotBefore = coordinator.CurrentSnapshot;

        changes.Publish(new BookCommittedChange.MetadataCommitted("book-2"));
        changes.Publish(new BookCommittedChange.BookRemoved("book-2"));
        changes.Publish(new BookCommittedChange.ActiveCatalogCommitted("book-2", "other-source", "new"));

        Assert.Equal(snapshotBefore, coordinator.CurrentSnapshot);
    }

}
