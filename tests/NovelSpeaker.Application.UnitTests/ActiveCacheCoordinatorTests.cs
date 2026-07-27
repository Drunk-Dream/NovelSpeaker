using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.ActiveCache;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class ActiveCacheCoordinatorTests
{
    [Fact]
    public async Task StartAsync_freezes_batch_and_processes_chapters_and_segments_in_book_order()
    {
        var content = new FakeContentService();
        var rules = new MutableRuleProvider(CreateRule(7, "批次规则"));
        var audio = new ControlledAudioProvider();
        var firstCall = audio.EnqueuePending();
        await using var coordinator = new ActiveCacheCoordinator(content, rules, audio);

        var start = await coordinator.StartAsync(
            new StartActiveCacheRequest("book-1", [8, 3], 12),
            CancellationToken.None);
        await firstCall.Started;

        Assert.Equal(ActiveCacheBatchStatus.Running, coordinator.CurrentSnapshot!.Status);
        Assert.Equal(3, coordinator.CurrentSnapshot.CurrentChapterIndex);
        Assert.Equal(ActiveCacheChapterStatus.Running, coordinator.CurrentSnapshot.Chapters[0].Status);
        Assert.Equal(0, coordinator.CurrentSnapshot.CompletedSegmentCount);
        Assert.Equal(3, coordinator.CurrentSnapshot.TotalSegmentCount);

        content.SpeechTextSuffix = "-changed";
        rules.Current = CreateRule(9, "新规则");
        firstCall.CompleteUsingCache();
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);

        Assert.Equal(ActiveCacheStartStatus.Accepted, start.Status);
        Assert.Equal(
            [(3, 0, "三-一"), (3, 1, "三-二"), (8, 0, "八-一")],
            audio.Calls.Select(call => (
                call.Request.ChapterIndex,
                call.Request.SegmentIndex,
                call.Request.SpeechText)));
        Assert.All(audio.Calls, call => Assert.Equal(7, call.Request.RuleId));
        Assert.All(audio.Calls, call => Assert.Equal(12, call.Request.SpeakSpeed));
        Assert.All(audio.Calls, call => Assert.Equal(PlaybackAudioPriority.ActiveCache, call.Priority));
        Assert.Equal(ActiveCacheBatchStatus.Completed, coordinator.CurrentSnapshot!.Status);
        Assert.Equal(2, coordinator.CurrentSnapshot.CompletedChapterCount);
        Assert.Equal(3, coordinator.CurrentSnapshot.CompletedSegmentCount);
        Assert.Equal(
            [ActiveCacheChapterStatus.Completed, ActiveCacheChapterStatus.Completed],
            coordinator.CurrentSnapshot.Chapters.Select(chapter => chapter.Status));
    }

    [Fact]
    public async Task StartAsync_rejects_second_batch_and_cancel_preserves_completed_progress()
    {
        var audio = new ControlledAudioProvider();
        audio.EnqueueSuccess();
        var pending = audio.EnqueuePending();
        await using var coordinator = new ActiveCacheCoordinator(
            new FakeContentService(),
            new MutableRuleProvider(CreateRule(7, "批次规则")),
            audio);

        var first = await coordinator.StartAsync(
            new StartActiveCacheRequest("book-1", [3], 10),
            CancellationToken.None);
        await pending.Started;
        var second = await coordinator.StartAsync(
            new StartActiveCacheRequest("book-1", [8], 10),
            CancellationToken.None);

        await coordinator.CancelAsync(CancellationToken.None);

        Assert.Equal(ActiveCacheStartStatus.Accepted, first.Status);
        Assert.Equal(ActiveCacheStartStatus.BatchAlreadyActive, second.Status);
        Assert.Equal(ActiveCacheBatchStatus.Cancelled, coordinator.CurrentSnapshot!.Status);
        Assert.Equal(1, coordinator.CurrentSnapshot.CompletedSegmentCount);
        Assert.Null(coordinator.CurrentSnapshot.ErrorSummary);
        Assert.True(audio.Calls[1].CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Failed_audio_stops_batch_and_publishes_only_safe_failure_summary()
    {
        var audio = new ControlledAudioProvider();
        audio.EnqueueFailure(new TtsExecutionFailure(
            TtsErrorKind.Network,
            "安全错误摘要",
            null,
            null,
            null,
            null));
        await using var coordinator = new ActiveCacheCoordinator(
            new FakeContentService(),
            new MutableRuleProvider(CreateRule(7, "批次规则")),
            audio);

        await coordinator.StartAsync(
            new StartActiveCacheRequest("book-1", [3, 8], 10),
            CancellationToken.None);
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);

        Assert.Equal(ActiveCacheBatchStatus.Failed, coordinator.CurrentSnapshot!.Status);
        Assert.Equal("安全错误摘要", coordinator.CurrentSnapshot.ErrorSummary);
        Assert.Equal(ActiveCacheChapterStatus.Failed, coordinator.CurrentSnapshot.Chapters[0].Status);
        Assert.Equal(ActiveCacheChapterStatus.Pending, coordinator.CurrentSnapshot.Chapters[1].Status);
        Assert.Single(audio.Calls);
    }

    [Fact]
    public async Task Accepted_batch_is_not_owned_by_the_starting_callers_cancellation_token()
    {
        var audio = new ControlledAudioProvider();
        var pending = audio.EnqueuePending();
        await using var coordinator = new ActiveCacheCoordinator(
            new FakeContentService(),
            new MutableRuleProvider(CreateRule(7, "批次规则")),
            audio);
        using var pageOperation = new CancellationTokenSource();

        await coordinator.StartAsync(
            new StartActiveCacheRequest("book-1", [8], 10),
            pageOperation.Token);
        await pending.Started;
        pageOperation.Cancel();
        pending.CompleteUsingCache();
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);

        Assert.False(audio.Calls[0].CancellationToken.IsCancellationRequested);
        Assert.Equal(ActiveCacheBatchStatus.Completed, coordinator.CurrentSnapshot!.Status);
    }

    [Fact]
    public async Task Playback_preemption_does_not_cancel_or_fail_the_batch()
    {
        var audio = new ControlledAudioProvider();
        audio.EnqueueFailure(new TtsExecutionFailure(
            TtsErrorKind.Cancelled,
            "已取消当前音频生成。",
            null,
            null,
            null,
            null));
        audio.EnqueueSuccess();
        await using var coordinator = new ActiveCacheCoordinator(
            new FakeContentService(),
            new MutableRuleProvider(CreateRule(7, "批次规则")),
            audio);

        await coordinator.StartAsync(
            new StartActiveCacheRequest("book-1", [8], 10),
            CancellationToken.None);
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);

        Assert.Equal(2, audio.Calls.Count);
        Assert.Equal(ActiveCacheBatchStatus.Completed, coordinator.CurrentSnapshot!.Status);
        Assert.Equal(1, coordinator.CurrentSnapshot.CompletedSegmentCount);
    }

    private static HttpTtsRule CreateRule(long id, string name) =>
        TestHttpTtsRules.Create(
            id,
            name,
            "https://example.com/tts?text={{speakText}}",
            "audio/mpeg",
            null,
            null,
            null,
            null,
            true,
            null,
            "2026-07-20T00:00:00.0000000Z",
            "2026-07-20T00:00:00.0000000Z");

    private sealed class FakeContentService : IBookPlaybackContentService
    {
        public string SpeechTextSuffix { get; set; } = string.Empty;

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<PlaybackBookContent?>(new PlaybackBookContent(
                bookId,
                "测试书",
                [
                    PlaybackChapterContent.Unloaded(3, "第三章"),
                    PlaybackChapterContent.Unloaded(8, "第八章")
                ]));
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SpeechSegment> segments = chapterIndex switch
            {
                3 =>
                [
                    new SpeechSegment(0, 0, 3, "三-一", $"三-一{SpeechTextSuffix}"),
                    new SpeechSegment(1, 4, 3, "三-二", $"三-二{SpeechTextSuffix}")
                ],
                8 => [new SpeechSegment(0, 0, 3, "八-一", $"八-一{SpeechTextSuffix}")],
                _ => []
            };
            return Task.FromResult<PlaybackChapterContent?>(
                PlaybackChapterContent.FromLoaded(chapterIndex, $"章节 {chapterIndex}", segments));
        }
    }

    private sealed class MutableRuleProvider : ISelectedTtsRuleProvider
    {
        public MutableRuleProvider(HttpTtsRule rule)
        {
            Current = rule;
        }

        public HttpTtsRule Current { get; set; }

        public Task<SelectedPlaybackRule?> GetSelectedRuleAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<SelectedPlaybackRule?>(new SelectedPlaybackRule(
                Current.Id,
                Current.Name,
                Current,
                new TtsRuleNormalizer().Normalize(Current)));
        }

        public Task<SelectedPlaybackRule?> SelectRuleAsync(long ruleId, CancellationToken cancellationToken) =>
            GetSelectedRuleAsync(cancellationToken);
    }

    private sealed class ControlledAudioProvider : IPlaybackAudioProvider
    {
        private readonly Queue<Func<Call, Task<PlaybackAudioResult>>> _responses = [];

        public List<Call> Calls { get; } = [];

        public void EnqueueSuccess() =>
            _responses.Enqueue(static _ => Task.FromResult(new PlaybackAudioResult("cached.mp3", true, null)));

        public PendingCall EnqueuePending()
        {
            var pending = new PendingCall();
            _responses.Enqueue(pending.RunAsync);
            return pending;
        }

        public void EnqueueFailure(TtsExecutionFailure failure) =>
            _responses.Enqueue(_ => Task.FromResult(new PlaybackAudioResult(null, false, failure)));

        public Task<PlaybackAudioResult> GetAudioAsync(
            PlaybackAudioRequest request,
            PlaybackAudioPriority priority,
            Action<PlaybackAudioProgress>? progressCallback,
            CancellationToken cancellationToken)
        {
            var call = new Call(request, priority, cancellationToken);
            Calls.Add(call);
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : static (Call _) => Task.FromResult(new PlaybackAudioResult("generated.mp3", false, null));
            return response(call);
        }

        public Task InvalidateAsync(PlaybackAudioRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed record Call(
        PlaybackAudioRequest Request,
        PlaybackAudioPriority Priority,
        CancellationToken CancellationToken);

    private sealed class PendingCall
    {
        private readonly TaskCompletionSource<PlaybackAudioResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public async Task<PlaybackAudioResult> RunAsync(Call call)
        {
            _started.TrySetResult(true);
            return await _completion.Task.WaitAsync(call.CancellationToken);
        }

        public void CompleteUsingCache() =>
            _completion.TrySetResult(new PlaybackAudioResult("cached.mp3", true, null));
    }
}
