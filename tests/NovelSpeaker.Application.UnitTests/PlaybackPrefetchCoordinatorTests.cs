using System.Collections.Concurrent;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackPrefetchCoordinatorTests
{
    [Fact]
    public async Task Restored_window_retries_cancelled_shared_synthesis_and_preserves_ready_cache()
    {
        var runtime = new GatedSynthesisRuntime();
        var provider = new ResolvedSpeechProvider(TestSpeechProviders.Item(1, "默认服务"), runtime);
        var sessionId = Guid.NewGuid();
        var request = Request(provider, sessionId, 1);
        var ready = new AudioCacheEntry(Request(provider, sessionId, 0).ToCacheKey(), "ready.mp3");
        var store = new PrefetchAudioStore();
        store.Entries[ready.Key] = ready;
        var audio = new ObservedGeneration(new CacheAudioGenerationProvider(store, store));
        var coordinator = new PlaybackPrefetchCoordinator(audio, new SelectedProvider(provider),
            new FakeSettings(AppSettings.Default with { DefaultSpeakSpeed = 10 }));
        try
        {
            await coordinator.SubmitAsync(new PlaybackPrefetchWindow(sessionId, [request])
            { Revision = 1 }, CancellationToken.None);
            await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.SubmitAsync(new PlaybackPrefetchWindow(sessionId, [])
            { Revision = 2 }, CancellationToken.None);
            Assert.True(runtime.FirstToken.IsCancellationRequested);
            Assert.Same(ready, store.Entries[ready.Key]);

            await coordinator.SubmitAsync(new PlaybackPrefetchWindow(sessionId, [request])
            { Revision = 3 }, CancellationToken.None);
            // Signalled after the real generator has joined the still-blocked old execution.
            await audio.Rejoined.Task.WaitAsync(TimeSpan.FromSeconds(5));
            runtime.Release.TrySetResult(new(runtime.OldAudio, "audio/mpeg", null));
            var resumed = await audio.ResumedAudio!.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(resumed.IsSuccess);
            Assert.Equal(2, runtime.CallCount);
            Assert.Equal(1, store.WriteCount);
            Assert.Equal(2, store.Entries.Count);
            Assert.Same(ready, store.Entries[ready.Key]);
            Assert.Equal(resumed.FilePath, store.Entries[request.ToCacheKey()].FilePath);
            Assert.False(runtime.OldAudio.CanRead);
        }
        finally
        {
            runtime.Release.TrySetResult(new(runtime.OldAudio, "audio/mpeg", null));
            await coordinator.CancelAsync(sessionId, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Older_window_submission_cannot_replace_a_newer_session_window()
    {
        var provider = TestSpeechProviders.Resolve(TestSpeechProviders.Item(1, "默认规则"));
        var audio = new GatedAudioProvider();
        var coordinator = new PlaybackPrefetchCoordinator(audio, new SelectedProvider(provider),
            new FakeSettings(AppSettings.Default));
        var sessionId = Guid.NewGuid();

        await coordinator.SubmitAsync(new PlaybackPrefetchWindow(sessionId, [Request(provider, sessionId, 1)])
        { Revision = 2 }, CancellationToken.None);
        await audio.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.SubmitAsync(new PlaybackPrefetchWindow(sessionId, [Request(provider, sessionId, 0)])
        { Revision = 1 }, CancellationToken.None);
        audio.Release.TrySetResult(new("latest.mp3", false, null));
        await audio.RequestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("latest", Assert.Single(audio.Requests).SpeechText);
        await coordinator.CancelAsync(sessionId, CancellationToken.None);
    }

    [Fact]
    public async Task Replacing_future_window_preserves_the_active_current_target_request()
    {
        var provider = TestSpeechProviders.Resolve(TestSpeechProviders.Item(1, "默认规则"));
        var audio = new GatedAudioProvider();
        var coordinator = new PlaybackPrefetchCoordinator(audio, new SelectedProvider(provider),
            new FakeSettings(AppSettings.Default));
        var sessionId = Guid.NewGuid();
        var currentTarget = Request(provider, sessionId, 1);

        await coordinator.SubmitAsync(new PlaybackPrefetchWindow(sessionId,
            [currentTarget, Request(provider, sessionId, 2)])
        { Revision = 1 }, CancellationToken.None);
        await audio.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var currentRequestToken = audio.RequestToken;

        await coordinator.SubmitAsync(new PlaybackPrefetchWindow(sessionId,
            [Request(provider, sessionId, 2)])
        {
            Revision = 2,
            KeepActiveKey = currentTarget.ToCacheKey()
        }, CancellationToken.None);

        Assert.False(currentRequestToken.IsCancellationRequested);
        audio.Release.TrySetResult(new("current-target.mp3", false, null));
        await audio.RequestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.CancelAsync(sessionId, CancellationToken.None);
    }

    private static AudioGenerationRequest Request(ResolvedSpeechProvider provider, Guid sessionId, int index) =>
        new("book-1", 0, index, index == 0 ? "stale" : "latest", provider, 10, sessionId)
        {
            ChapterId = "book-1/chapter/0",
            StableSegmentIdentity = StableSpeechSegmentIdentity.Body(0, index + 1)
        };

    private sealed class SelectedProvider(ResolvedSpeechProvider provider) : TestCurrentSpeechProvider
    {
        public override Task<ResolvedSpeechProvider?> GetSelectedProviderAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ResolvedSpeechProvider?>(provider);
    }

    private sealed class GatedAudioProvider : IAudioGenerationProvider
    {
        public List<AudioGenerationRequest> Requests { get; } = [];
        public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AudioGenerationResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RequestCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken RequestToken { get; private set; }

        public Task<AudioGenerationResult> GetAudioAsync(AudioGenerationRequest request,
            AudioGenerationPriority priority, Action<AudioGenerationProgress>? progressCallback,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestToken = cancellationToken;
            RequestStarted.TrySetResult();
            return CompleteFirstAsync();
        }

        private async Task<AudioGenerationResult> CompleteFirstAsync()
        {
            var result = await Release.Task.ConfigureAwait(false);
            RequestCompleted.TrySetResult();
            return result;
        }

        public Task InvalidateAsync(AudioGenerationRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSettings(AppSettings current) : IAppSettingsService
    {
        public AppSettings Current => current;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }
        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ObservedGeneration(IAudioGenerationProvider inner) : IAudioGenerationProvider
    {
        private int _calls;
        public TaskCompletionSource Rejoined { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AudioGenerationResult>? ResumedAudio { get; private set; }

        public Task<AudioGenerationResult> GetAudioAsync(AudioGenerationRequest request,
            AudioGenerationPriority priority, Action<AudioGenerationProgress>? progressCallback,
            CancellationToken cancellationToken)
        {
            var task = inner.GetAudioAsync(request, priority, progressCallback, cancellationToken);
            if (Interlocked.Increment(ref _calls) == 2)
            {
                ResumedAudio = task;
                Rejoined.TrySetResult();
            }
            return task;
        }

        public Task InvalidateAsync(AudioGenerationRequest request, CancellationToken cancellationToken) =>
            inner.InvalidateAsync(request, cancellationToken);
    }

    private sealed class GatedSynthesisRuntime : IProviderRuntime
    {
        public SpeechProviderType Type => SpeechProviderType.Http;
        public int CallCount { get; private set; }
        public CancellationToken FirstToken { get; private set; }
        public MemoryStream OldAudio { get; } = new([1, 2, 3]);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ProviderSynthesisResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProviderSynthesisResult> SynthesizeAsync(SpeechProviderInstance provider,
            ProviderSynthesisRequest request, CancellationToken cancellationToken)
        {
            if (++CallCount != 1)
                return Task.FromResult(new ProviderSynthesisResult(new MemoryStream([4, 5, 6]), "audio/mpeg", null));
            FirstToken = cancellationToken;
            Started.TrySetResult();
            return Release.Task;
        }
    }

    private sealed class PrefetchAudioStore : IAudioCache, IGeneratedAudioFileStore
    {
        public ConcurrentDictionary<AudioCacheKey, AudioCacheEntry> Entries { get; } = new();
        public int WriteCount { get; private set; }

        public Task<AudioCacheEntry?> TryGetAsync(AudioCacheKey key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Entries.GetValueOrDefault(key));
        }

        public Task<TtsAudioResponse> WriteAsync(ProviderSynthesisResult audio, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            return Task.FromResult(new TtsAudioResponse("generated.mp3", 200, "audio/mpeg", "mp3"));
        }

        public Task<AudioCacheEntry> StoreAsync(AudioCacheWriteRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = new AudioCacheEntry(request.Key, request.SourceFilePath);
            Entries[entry.Key] = entry;
            return Task.FromResult(entry);
        }

        public Task InvalidateAsync(AudioCacheKey key, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
