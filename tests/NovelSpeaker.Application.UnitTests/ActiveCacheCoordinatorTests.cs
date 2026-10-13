using System.Collections.Concurrent;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.ActiveCache;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class ActiveCacheCoordinatorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Process_stop_tracks_external_synthesis_and_rejects_late_audio(bool cooperative, bool joinPlayback)
    {
        var runtime = new ShutdownProviderRuntime(cooperative);
        var provider = new ShutdownCurrentProvider(runtime);
        var store = new ShutdownAudioStore();
        var audio = new CacheAudioGenerationProvider(store, store);
        var coordinator = new ActiveCacheCoordinator(new FakeContentService(), provider, audio, new BookMutationGate());
        using var playbackCancellation = new CancellationTokenSource();
        Task<AudioGenerationResult>? playback = null;
        try
        {
            if (joinPlayback)
            {
                var segment = (await new FakeContentService().GetChapterAsync("book-1", 8, CancellationToken.None))!.Segments[0];
                playback = audio.GetAudioAsync(new("book-1", 8, 0, segment.SpeechText,
                    (await provider.GetSelectedProviderAsync(CancellationToken.None))!, 10, Guid.NewGuid())
                {
                    StableSegmentIdentity = segment.StableIdentity
                }, AudioGenerationPriority.Current, null, playbackCancellation.Token);
                await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            await coordinator.StartAsync(new("book-1", [8], 10), CancellationToken.None);
            await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Playback remains the shared synthesis owner. Process shutdown stops it
            // before draining cache jobs; active-cache cancellation cannot take it over.
            if (joinPlayback) playbackCancellation.Cancel();
            using var budget = new CancellationTokenSource();
            var stop = coordinator.StopAsync(budget.Token);
            Assert.True(runtime.Token.IsCancellationRequested);
            if (cooperative)
            {
                await stop.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(ActiveCacheBatchStatus.Cancelled, coordinator.CurrentSnapshot!.Status);
            }
            else
            {
                Assert.False(stop.IsCompleted);
                budget.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(7));
                Assert.Equal(ActiveCacheBatchStatus.Cancelling, coordinator.CurrentSnapshot!.Status);
                Assert.Equal(0, coordinator.CurrentSnapshot.CompletedSegmentCount);
            }
        }
        finally
        {
            runtime.Release.TrySetResult(new(runtime.Audio, "audio/mpeg", null));
            await coordinator.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.DisposeAsync();
            if (playback is not null) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playback);
        }

        Assert.Equal(ActiveCacheBatchStatus.Cancelled, coordinator.CurrentSnapshot!.Status);
        Assert.Equal(0, store.WriteCount);
        if (!cooperative) Assert.False(runtime.Audio.CanRead);
    }

    [Fact]
    public async Task Process_stop_cancels_immediately_rejects_new_work_and_preserves_completed_progress()
    {
        var audio = new ControlledAudioProvider();
        audio.EnqueueSuccess();
        var pending = audio.EnqueuePending(holdCancellation: true);
        await using var coordinator = new ActiveCacheCoordinator(new FakeContentService(),
            new MutableRuleProvider(CreateRule(7, "Provider")), audio, new BookMutationGate());
        await coordinator.StartAsync(new("book-1", [3, 8], 10), CancellationToken.None);
        await pending.Started;

        var stop = coordinator.StopAsync(CancellationToken.None);
        Assert.True(audio.Calls[1].CancellationToken.IsCancellationRequested);
        Assert.False(stop.IsCompleted);
        Assert.Equal(ActiveCacheBatchStatus.Cancelling, coordinator.CurrentSnapshot!.Status);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            coordinator.StartAsync(new("book-1", [8], 10), CancellationToken.None));
        pending.ReleaseCancellation();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ActiveCacheBatchStatus.Cancelled, coordinator.CurrentSnapshot.Status);
        Assert.Equal(1, coordinator.CurrentSnapshot.CompletedSegmentCount);
        Assert.Equal(2, audio.Calls.Count);
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Process_stop_cancels_admission_already_in_progress()
    {
        var mutations = new BookMutationGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = mutations.RunAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            return true;
        }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var audio = new ControlledAudioProvider();
        await using var coordinator = new ActiveCacheCoordinator(new FakeContentService(),
            new MutableRuleProvider(CreateRule(7, "Provider")), audio, mutations);
        try
        {
            var start = coordinator.StartAsync(new("book-1", [3], 10), CancellationToken.None);
            var stop = coordinator.StopAsync(CancellationToken.None);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(TimeSpan.FromSeconds(5)));
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(coordinator.CurrentSnapshot);
            Assert.Empty(audio.Calls);
        }
        finally
        {
            release.TrySetResult();
            await holder;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Throwing_observer_does_not_prevent_worker_progress_terminal_result_or_retry(bool fail)
    {
        var audio = new ControlledAudioProvider();
        var pending = audio.EnqueuePending();
        if (fail) audio.EnqueueException();
        else audio.EnqueueSuccess();
        await using var coordinator = new ActiveCacheCoordinator(new FakeContentService(),
            new MutableRuleProvider(CreateRule(7, "Provider")), audio, new BookMutationGate());
        var snapshots = new ConcurrentQueue<ActiveCacheSnapshot>();
        coordinator.SnapshotChanged += (_, _) => throw new InvalidOperationException("fixture observer failure");
        coordinator.SnapshotChanged += (_, snapshot) => snapshots.Enqueue(snapshot);

        var start = await coordinator.StartAsync(new("book-1", [3], 10), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ActiveCacheStartStatus.Accepted, start.Status);
        await pending.Started;
        pending.CompleteUsingCache();
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        var terminal = fail ? ActiveCacheBatchStatus.Failed : ActiveCacheBatchStatus.Completed;
        Assert.Equal(terminal, coordinator.CurrentSnapshot!.Status);
        Assert.Contains(snapshots, snapshot => snapshot.Status == ActiveCacheBatchStatus.Waiting);
        Assert.Contains(snapshots, snapshot => snapshot.Status == ActiveCacheBatchStatus.Running);
        Assert.Contains(snapshots, snapshot => snapshot.CompletedSegmentCount > 0);
        Assert.Contains(snapshots, snapshot => snapshot.Status == terminal);
        if (fail) Assert.Equal("主动缓存失败，请重试。", coordinator.CurrentSnapshot.ErrorSummary);

        var retry = await coordinator.StartAsync(new("book-1", [8], 10), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ActiveCacheStartStatus.Accepted, retry.Status);
        Assert.NotEqual(start.BatchId, retry.BatchId);
        Assert.Equal(ActiveCacheBatchStatus.Completed, coordinator.CurrentSnapshot.Status);
    }

    [Fact]
    public async Task Throwing_observer_does_not_block_duplicate_rejection_cancel_restart_or_dispose()
    {
        var audio = new ControlledAudioProvider();
        audio.EnqueueSuccess();
        var pending = audio.EnqueuePending(holdCancellation: true);
        await using var coordinator = new ActiveCacheCoordinator(new FakeContentService(),
            new MutableRuleProvider(CreateRule(7, "Provider")), audio, new BookMutationGate());
        var snapshots = new ConcurrentQueue<ActiveCacheSnapshot>();
        coordinator.SnapshotChanged += (_, _) => throw new InvalidOperationException("fixture observer failure");
        coordinator.SnapshotChanged += (_, snapshot) => snapshots.Enqueue(snapshot);

        var start = await coordinator.StartAsync(new("book-1", [3], 10), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = await coordinator.StartAsync(new("book-1", [8], 10), CancellationToken.None);
        await pending.Started;
        Assert.Equal(ActiveCacheStartStatus.Accepted, start.Status);
        Assert.Equal(ActiveCacheStartStatus.BatchAlreadyActive, duplicate.Status);
        var cancel = coordinator.CancelAsync(CancellationToken.None);
        pending.ReleaseCancellation();
        await cancel.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(audio.Calls[1].CancellationToken.IsCancellationRequested);
        Assert.Equal(ActiveCacheBatchStatus.Cancelled, coordinator.CurrentSnapshot!.Status);
        Assert.Equal(1, coordinator.CurrentSnapshot.CompletedSegmentCount);
        Assert.Contains(snapshots, snapshot => snapshot.Status == ActiveCacheBatchStatus.Cancelling);
        Assert.Contains(snapshots, snapshot => snapshot.Status == ActiveCacheBatchStatus.Cancelled);

        var replacementAudio = audio.EnqueuePending();
        var replacement = await coordinator.StartAsync(new("book-1", [8], 10), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await replacementAudio.Started;
        Assert.Equal(ActiveCacheStartStatus.Accepted, replacement.Status);
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(audio.Calls[2].CancellationToken.IsCancellationRequested);
        Assert.Equal(ActiveCacheBatchStatus.Cancelled, coordinator.CurrentSnapshot.Status);
        Assert.Contains(snapshots, snapshot => snapshot.BatchId == replacement.BatchId &&
            snapshot.Status == ActiveCacheBatchStatus.Cancelled);
    }

    [Fact]
    public async Task Source_removal_cancels_only_its_frozen_active_cache_batch_and_waits_for_completion()
    {
        var audio = new ControlledAudioProvider();
        var pending = audio.EnqueuePending();
        await using var coordinator = new ActiveCacheCoordinator(new FakeContentService(),
            new MutableRuleProvider(CreateRule(7, "批次规则")), audio, new BookMutationGate());
        await coordinator.StartAsync(new("book-1", [8], 10), CancellationToken.None);
        await pending.Started;
        await coordinator.StopForRemovalAsync("book-1", "other-source", CancellationToken.None);
        Assert.False(audio.Calls[0].CancellationToken.IsCancellationRequested);
        await coordinator.StopForRemovalAsync("book-1", "local:book-1", CancellationToken.None);
        Assert.True(audio.Calls[0].CancellationToken.IsCancellationRequested);
        Assert.Equal(ActiveCacheBatchStatus.Cancelled, coordinator.CurrentSnapshot!.Status);
        Assert.Equal(0, coordinator.CurrentSnapshot.CompletedSegmentCount);
    }

    [Fact]
    public async Task Cache_batch_skips_fully_cached_chapter_and_fills_missing_segments()
    {
        var audio = new ControlledAudioProvider();
        audio.EnqueueSuccess();
        audio.EnqueueSuccess();
        await using var coordinator = new ActiveCacheCoordinator(new FakeContentService(), new MutableRuleProvider(CreateRule(7, "Provider")), audio, new BookMutationGate());
        await coordinator.StartAsync(new StartActiveCacheRequest("book-1", [3, 8], 10), CancellationToken.None);
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);
        Assert.Equal([ActiveCacheChapterStatus.Skipped, ActiveCacheChapterStatus.Completed], coordinator.CurrentSnapshot!.Chapters.Select(chapter => chapter.Status));
        Assert.Equal(3, coordinator.CurrentSnapshot.CompletedSegmentCount);
    }

    [Fact]
    public async Task StartAsync_freezes_batch_and_processes_chapters_and_segments_in_book_order()
    {
        var content = new FakeContentService();
        var rules = new MutableRuleProvider(CreateRule(7, "批次规则"));
        var audio = new ControlledAudioProvider();
        var firstCall = audio.EnqueuePending();
        await using var coordinator = new ActiveCacheCoordinator(content, rules, audio, new BookMutationGate());

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
            [(3, 0, "三-一"), (3, 2, "三-二"), (8, 0, "八-一")],
            audio.Calls.Select(call => (
                call.Request.ChapterIndex,
                call.Request.SegmentIndex,
                call.Request.SpeechText)));
        Assert.All(audio.Calls, call => Assert.Equal(TestSpeechProviders.Id(7), call.Request.Provider.ProviderId));
        Assert.All(audio.Calls, call => Assert.Equal(12, call.Request.SpeakSpeed));
        Assert.All(audio.Calls, call => Assert.Equal(AudioGenerationPriority.ActiveCache, call.Priority));
        var activeIdentity = audio.Calls[0].Request;
        var playbackIdentity = activeIdentity with
        {
            SegmentIndex = 99,
            SessionId = Guid.NewGuid()
        };
        Assert.Equal(activeIdentity.ToCacheKey(), playbackIdentity.ToCacheKey());
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
            audio, new BookMutationGate());

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
    public async Task CancelAsync_does_not_cancel_replacement_batch_when_old_batch_finishes_during_notification()
    {
        var audio = new ControlledAudioProvider();
        var oldBatchAudio = audio.EnqueuePending();
        var replacementAudio = audio.EnqueuePending();
        await using var coordinator = new ActiveCacheCoordinator(
            new FakeContentService(),
            new MutableRuleProvider(CreateRule(7, "批次规则")),
            audio,
            new BookMutationGate());

        var start = await coordinator.StartAsync(
            new StartActiveCacheRequest("book-1", [8], 10),
            CancellationToken.None);
        await oldBatchAudio.Started;

        var notificationHandled = false;
        ActiveCacheStartResult? replacement = null;
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (notificationHandled || snapshot.Status != ActiveCacheBatchStatus.Cancelling)
            {
                return;
            }

            notificationHandled = true;
            coordinator.WaitForCurrentBatchAsync(CancellationToken.None).GetAwaiter().GetResult();
            replacement = coordinator.StartAsync(
                new StartActiveCacheRequest("book-1", [3], 10),
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await coordinator.CancelAsync(CancellationToken.None);
        await replacementAudio.Started;

        Assert.Equal(ActiveCacheStartStatus.Accepted, start.Status);
        Assert.Equal(ActiveCacheStartStatus.Accepted, replacement?.Status);
        Assert.True(audio.Calls[0].CancellationToken.IsCancellationRequested);
        Assert.False(audio.Calls[1].CancellationToken.IsCancellationRequested);

        await coordinator.CancelAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Failed_chapter_preserves_safe_summary_and_continues_remaining_chapters()
    {
        foreach (var unexpectedException in new[] { false, true })
        {
            var audio = new ControlledAudioProvider();
            if (unexpectedException) audio.EnqueueException();
            else audio.EnqueueFailure(new TtsExecutionFailure(
                TtsErrorKind.Network,
                "安全错误摘要",
                null,
                null,
                null,
                null));
            await using var coordinator = new ActiveCacheCoordinator(
                new FakeContentService(),
                new MutableRuleProvider(CreateRule(7, "批次规则")),
                audio, new BookMutationGate());

            await coordinator.StartAsync(
                new StartActiveCacheRequest("book-1", [3, 8], 10),
                CancellationToken.None);
            await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);

            Assert.Equal(ActiveCacheBatchStatus.Failed, coordinator.CurrentSnapshot!.Status);
            Assert.Equal(unexpectedException ? "主动缓存失败，请重试。" : "安全错误摘要", coordinator.CurrentSnapshot.ErrorSummary);
            Assert.Equal(ActiveCacheChapterStatus.Failed, coordinator.CurrentSnapshot.Chapters[0].Status);
            Assert.Equal(ActiveCacheChapterStatus.Completed, coordinator.CurrentSnapshot.Chapters[1].Status);
            Assert.Equal([3, 8], audio.Calls.Select(call => call.Request.ChapterIndex));
        }
    }

    [Fact]
    public async Task Accepted_batch_is_not_owned_by_the_starting_callers_cancellation_token()
    {
        var audio = new ControlledAudioProvider();
        var pending = audio.EnqueuePending();
        await using var coordinator = new ActiveCacheCoordinator(
            new FakeContentService(),
            new MutableRuleProvider(CreateRule(7, "批次规则")),
            audio, new BookMutationGate());
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
            audio, new BookMutationGate());

        await coordinator.StartAsync(
            new StartActiveCacheRequest("book-1", [8], 10),
            CancellationToken.None);
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);

        Assert.Equal(2, audio.Calls.Count);
        Assert.Equal(ActiveCacheBatchStatus.Completed, coordinator.CurrentSnapshot!.Status);
        Assert.Equal(1, coordinator.CurrentSnapshot.CompletedSegmentCount);
    }

    private static SpeechProviderInstance CreateRule(long id, string name) =>
        TestSpeechProviders.Create(id, name, "https://example.com/tts?text={{speakText}}");

    private sealed class ShutdownCurrentProvider(IProviderRuntime runtime) : TestCurrentSpeechProvider
    {
        public override Task<ResolvedSpeechProvider?> GetSelectedProviderAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ResolvedSpeechProvider?>(new(CreateRule(7, "Provider"), runtime));
    }

    private sealed class ShutdownProviderRuntime(bool cooperative) : IProviderRuntime
    {
        public SpeechProviderType Type => SpeechProviderType.Http;
        public CancellationToken Token { get; private set; }
        public MemoryStream Audio { get; } = new([1, 2, 3]);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ProviderSynthesisResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProviderSynthesisResult> SynthesizeAsync(SpeechProviderInstance provider,
            ProviderSynthesisRequest request, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.TrySetResult();
            return cooperative ? Release.Task.WaitAsync(cancellationToken) : Release.Task;
        }
    }

    private sealed class ShutdownAudioStore : IAudioCache, IGeneratedAudioFileStore
    {
        public int WriteCount { get; private set; }
        public Task<AudioCacheEntry?> TryGetAsync(AudioCacheKey key, CancellationToken cancellationToken) =>
            Task.FromResult<AudioCacheEntry?>(null);
        public Task<AudioCacheEntry> StoreAsync(AudioCacheWriteRequest request, CancellationToken cancellationToken)
        {
            WriteCount++;
            throw new InvalidOperationException("Cancelled synthesis must not commit cache audio.");
        }
        public Task<TtsAudioResponse> WriteAsync(ProviderSynthesisResult audio, CancellationToken cancellationToken)
        {
            WriteCount++;
            throw new InvalidOperationException("Cancelled synthesis must not create an audio file.");
        }
        public Task InvalidateAsync(AudioCacheKey key, CancellationToken cancellationToken) => Task.CompletedTask;
    }

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
                ], SourceContext: new("local:book-1", "catalog")));
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
                    new SpeechSegment(1, 4, 3, "章节分隔符", "………"),
                    new SpeechSegment(2, 8, 3, "三-二", $"三-二{SpeechTextSuffix}")
                ],
                8 => [new SpeechSegment(0, 0, 3, "八-一", $"八-一{SpeechTextSuffix}")],
                _ => []
            };
            return Task.FromResult<PlaybackChapterContent?>(
                PlaybackChapterContent.FromLoaded(chapterIndex, $"章节 {chapterIndex}", segments));
        }
    }

    private sealed class MutableRuleProvider : TestCurrentSpeechProvider
    {
        public MutableRuleProvider(SpeechProviderInstance rule)
        {
            Current = rule;
        }

        public SpeechProviderInstance Current { get; set; }

        public override Task<ResolvedSpeechProvider?> GetSelectedProviderAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ResolvedSpeechProvider?>(TestSpeechProviders.Resolve(Current));
        }

        public override Task<ResolvedSpeechProvider?> SelectProviderAsync(ProviderId ruleId, CancellationToken cancellationToken) =>
            GetSelectedProviderAsync(cancellationToken);
    }

    private sealed class ControlledAudioProvider : IAudioGenerationProvider
    {
        private readonly Queue<Func<Call, Task<AudioGenerationResult>>> _responses = [];

        public List<Call> Calls { get; } = [];

        public void EnqueueSuccess() =>
            _responses.Enqueue(static _ => Task.FromResult(new AudioGenerationResult("cached.mp3", true, null)));

        public void EnqueueException() => _responses.Enqueue(static _ => throw new IOException("sensitive test detail"));

        public PendingCall EnqueuePending(bool holdCancellation = false)
        {
            var pending = new PendingCall(holdCancellation);
            _responses.Enqueue(pending.RunAsync);
            return pending;
        }

        public void EnqueueFailure(TtsExecutionFailure failure) =>
            _responses.Enqueue(_ => Task.FromResult(new AudioGenerationResult(null, false, failure)));

        public Task<AudioGenerationResult> GetAudioAsync(
            AudioGenerationRequest request,
            AudioGenerationPriority priority,
            Action<AudioGenerationProgress>? progressCallback,
            CancellationToken cancellationToken)
        {
            var call = new Call(request, priority, cancellationToken);
            Calls.Add(call);
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : static (Call _) => Task.FromResult(new AudioGenerationResult("generated.mp3", false, null));
            return response(call);
        }

        public Task InvalidateAsync(AudioGenerationRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed record Call(
        AudioGenerationRequest Request,
        AudioGenerationPriority Priority,
        CancellationToken CancellationToken);

    private sealed class PendingCall(bool holdCancellation)
    {
        private readonly TaskCompletionSource _cancellationRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<AudioGenerationResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public async Task<AudioGenerationResult> RunAsync(Call call)
        {
            _started.TrySetResult(true);
            try
            {
                return await _completion.Task.WaitAsync(call.CancellationToken);
            }
            catch (OperationCanceledException) when (holdCancellation)
            {
                await _cancellationRelease.Task;
                throw;
            }
        }

        public void ReleaseCancellation() => _cancellationRelease.TrySetResult();

        public void CompleteUsingCache() =>
            _completion.TrySetResult(new AudioGenerationResult("cached.mp3", true, null));
    }
}
