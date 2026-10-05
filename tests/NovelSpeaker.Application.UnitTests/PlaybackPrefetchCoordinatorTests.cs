using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackPrefetchCoordinatorTests
{
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
}
