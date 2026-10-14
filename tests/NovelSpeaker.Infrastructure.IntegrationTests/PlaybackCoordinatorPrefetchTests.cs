using System.Collections.Concurrent;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache.ActiveCache;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed partial class PlaybackCoordinatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Paused_open_navigation_and_configuration_do_not_authorize_prefetch(int count)
    {
        var prefetch = new FakePrefetchScheduler();
        var generation = new FakeAudioGenerationProvider();
        var providers = new FakeCurrentSpeechProvider(CreateRuleSelection(1, "默认服务"));
        var replacement = CreateRuleSelection(2, "替换服务");
        providers.RegisterSelectable(replacement);
        await using var coordinator = CreateCoordinator(new FakeLocalAudioPlaybackCoordinator(),
            book: CreateTwoChapterBook(), audioProvider: generation, selectedProviderProvider: providers,
            prefetchScheduler: prefetch,
            appSettingsStore: new FakeAppSettingsStore(AppSettings.Default with { PrefetchCount = count }));

        await coordinator.OpenPausedAsync(new("book-1", 0, 0, null), CancellationToken.None);
        await coordinator.JumpToChapterAsync(1, CancellationToken.None);
        await coordinator.JumpToSegmentAsync(0, 0, CancellationToken.None);
        await coordinator.ChangeProviderAsync(replacement.ProviderId, CancellationToken.None);
        await coordinator.ChangeSpeedAsync(12, CancellationToken.None);

        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Empty(generation.Requests);
        Assert.NotEmpty(prefetch.ScheduleCalls);
        Assert.All(prefetch.ScheduleCalls, window => Assert.Empty(window.Requests));
        Assert.All(prefetch.KeepActiveKeys, key => Assert.Null(key));

        var windows = prefetch.ScheduleCalls.Count;
        await coordinator.ResumeAsync(CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        await WaitForAsync(prefetch, () => prefetch.ScheduleCalls.Count > windows);
        var requests = prefetch.ScheduleCalls.Last().Requests;
        if (count == 0) Assert.Empty(requests);
        else Assert.Equal(replacement.ProviderId, Assert.Single(requests).Provider.ProviderId);
        Assert.Single(generation.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pause_cancels_active_and_pending_prefetch_and_quick_resume_retries_cancelled_audio(bool timer)
    {
        var providers = new FakeCurrentSpeechProvider(CreateRuleSelection(1, "默认服务"));
        var settings = new FakeAppSettingsStore(AppSettings.Default with { PrefetchCount = 2 });
        var audio = new PausedPrefetchAudio();
        var prefetch = new FakePrefetchScheduler(new PlaybackPrefetchCoordinator(audio, providers, settings));
        var local = new FakeLocalAudioPlaybackCoordinator();
        var time = new ManualTimeProvider();
        var foreground = new FakeAudioGenerationProvider();
        await using var coordinator = CreateCoordinator(local, book: CreateSegmentBook(4),
            selectedProviderProvider: providers, appSettingsStore: settings, prefetchScheduler: prefetch,
            audioProvider: foreground, timeProvider: time);
        await using var activeCache = new ActiveCacheCoordinator(
            new FakeBookPlaybackContentService(CreateTwoChapterBook() with { BookId = "cache-book" }),
            providers, audio, new BookMutationGate());

        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await activeCache.StartAsync(new("cache-book", [1], 10), CancellationToken.None);
            await audio.ActiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (timer)
            {
                ((IPlaybackStopTimer)coordinator).ScheduleAfter(TimeSpan.FromMinutes(1));
                time.Advance(TimeSpan.FromMinutes(1));
                await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.State == PlaybackState.Paused);
                // Wait for the timer's serialized command to finish as well as its snapshot.
                await coordinator.ChangeSpeedAsync(10, CancellationToken.None);
            }
            else await coordinator.PauseAsync(CancellationToken.None);

            Assert.True(audio.FirstToken.IsCancellationRequested);
            Assert.Single(audio.Requests);
            Assert.Equal(1, local.StartCallCount);
            Assert.Equal(1, local.PauseCallCount);
            Assert.False(audio.ActiveToken.IsCancellationRequested);
            audio.ActiveRelease.TrySetResult();
            await activeCache.WaitForCurrentBatchAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ActiveCacheBatchStatus.Completed, activeCache.CurrentSnapshot!.Status);

            // Restore the same window while the cancelled, uncooperative request is still active.
            await coordinator.ResumeAsync(CancellationToken.None);
            await WaitForPlayingAsync(coordinator);
            await WaitForAsync(prefetch, () => prefetch.ScheduleCalls.Last().Requests.Count == 2);
            audio.Release.TrySetResult();
            await audio.WindowCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal([1, 1, 2], audio.Requests.ToArray());
            Assert.Equal(1, local.StartCallCount);
            Assert.Single(foreground.Requests);
        }
        finally
        {
            audio.Release.TrySetResult();
            audio.ActiveRelease.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_play_window_cannot_reactivate_pause_or_replace_resumed_window(bool resume)
    {
        var prefetch = new FakePrefetchScheduler();
        var local = new FakeLocalAudioPlaybackCoordinator();
        var content = new FakeBookPlaybackContentService(CreateSegmentBook(4));
        await using var coordinator = CreateCoordinator(local, bookContentService: content, prefetchScheduler: prefetch);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        await WaitForAsync(prefetch, () => prefetch.ScheduleCalls.Any(window => window.Requests.Count == 2));

        // Synchronous continuations let releasing the validation gate also finish the old refresh.
        var release = new TaskCompletionSource();
        content.CurrentContextGate = release;
        content.CurrentContextGateCall = content.CurrentContextCallCount + 2;
        content.CurrentContextRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await coordinator.ChangeSpeedAsync(11, CancellationToken.None);
        await content.CurrentContextRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await coordinator.PauseAsync(CancellationToken.None);
            Assert.Empty(prefetch.ScheduleCalls.Last().Requests);
            Assert.Null(prefetch.KeepActiveKeys.Last());
            if (resume)
            {
                content.CurrentContextGate = null;
                await coordinator.ChangeSpeedAsync(12, CancellationToken.None);
                await coordinator.ResumeAsync(CancellationToken.None);
                await WaitForAsync(prefetch, () => prefetch.ScheduleCalls.Last().Requests.Count == 2);
                Assert.All(prefetch.ScheduleCalls.Last().Requests, request => Assert.Equal(12, request.SpeakSpeed));
            }
            var windows = prefetch.ScheduleCalls.Count;
            release.SetResult();
            Assert.Equal(windows, prefetch.ScheduleCalls.Count);
            Assert.Equal(resume ? PlaybackState.Playing : PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        }
        finally { release.TrySetResult(); }
    }

    private sealed class PausedPrefetchAudio : IAudioGenerationProvider
    {
        public ConcurrentQueue<int> Requests { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WindowCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken FirstToken { get; private set; }
        public CancellationToken ActiveToken { get; private set; }
        public TaskCompletionSource ActiveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ActiveRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AudioGenerationResult> GetAudioAsync(AudioGenerationRequest request,
            AudioGenerationPriority priority, Action<AudioGenerationProgress>? progressCallback,
            CancellationToken cancellationToken)
        {
            if (priority == AudioGenerationPriority.ActiveCache)
            {
                ActiveToken = cancellationToken;
                ActiveStarted.TrySetResult();
                await ActiveRelease.Task.WaitAsync(cancellationToken);
                return new("active-cache.mp3", false, null);
            }
            Assert.Equal(AudioGenerationPriority.Prefetch, priority);
            Requests.Enqueue(request.SegmentIndex);
            if (Requests.Count == 1)
            {
                FirstToken = cancellationToken;
                Started.TrySetResult();
                await Release.Task;
            }
            if (request.SegmentIndex == 2) WindowCompleted.TrySetResult();
            return new("prefetched.mp3", false, null);
        }

        public Task InvalidateAsync(AudioGenerationRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
