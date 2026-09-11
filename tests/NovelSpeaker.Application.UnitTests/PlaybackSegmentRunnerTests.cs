using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackSegmentRunnerTests
{
    [Fact]
    public async Task RunAsync_starts_local_playback_for_a_cache_hit()
    {
        var audioProvider = new RecordingAudioProvider();
        audioProvider.Enqueue(new AudioGenerationResult("cached.mp3", true, null));
        var localCoordinator = new RecordingLocalAudioPlaybackCoordinator();
        await using var audioController = new PlaybackAudioController(localCoordinator);
        var runner = new PlaybackSegmentRunner(audioProvider, audioController);

        var result = await runner.RunAsync(
            new PlaybackSegmentRunRequest(CreateRequest(), "示例小说 · 第一章", 240, ForceInvalidate: false),
            null,
            CancellationToken.None);

        Assert.True(result.Audio.IsUsingCache);
        Assert.Equal("cached.mp3", localCoordinator.LastRequest?.FilePath);
        Assert.True(localCoordinator.LastRequest?.IsUsingCache == true);
        Assert.False(audioProvider.Invalidated);
    }

    [Fact]
    public async Task RunAsync_invalidates_before_generating_and_playing_when_requested()
    {
        var audioProvider = new RecordingAudioProvider();
        audioProvider.Enqueue(new AudioGenerationResult("generated.mp3", false, null));
        var localCoordinator = new RecordingLocalAudioPlaybackCoordinator();
        await using var audioController = new PlaybackAudioController(localCoordinator);
        var runner = new PlaybackSegmentRunner(audioProvider, audioController);

        await runner.RunAsync(
            new PlaybackSegmentRunRequest(CreateRequest(), "示例小说 · 第一章", 0, ForceInvalidate: true),
            null,
            CancellationToken.None);

        Assert.Equal(["invalidate", "get"], audioProvider.Calls);
        Assert.Equal("generated.mp3", localCoordinator.LastRequest?.FilePath);
    }

    [Fact]
    public async Task RunAsync_does_not_start_local_playback_when_audio_generation_fails()
    {
        var audioProvider = new RecordingAudioProvider();
        var failure = new TtsExecutionFailure(
            TtsErrorKind.Unauthorized,
            "鉴权失败。",
            401,
            null,
            null,
            null);
        audioProvider.Enqueue(new AudioGenerationResult(null, false, failure));
        var localCoordinator = new RecordingLocalAudioPlaybackCoordinator();
        await using var audioController = new PlaybackAudioController(localCoordinator);
        var runner = new PlaybackSegmentRunner(audioProvider, audioController);

        var result = await runner.RunAsync(
            new PlaybackSegmentRunRequest(CreateRequest(), "示例小说 · 第一章", 0, ForceInvalidate: false),
            null,
            CancellationToken.None);

        Assert.False(result.Audio.IsSuccess);
        Assert.Equal(TtsErrorKind.Unauthorized, result.Audio.Failure!.Kind);
        Assert.Null(localCoordinator.LastRequest);
    }

    [Fact]
    public async Task RunAsync_propagates_cancellation_without_projecting_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var audioProvider = new RecordingAudioProvider
        {
            ExceptionToThrow = new OperationCanceledException(cancellation.Token)
        };
        var runner = new PlaybackSegmentRunner(
            audioProvider,
            new PlaybackAudioController(new RecordingLocalAudioPlaybackCoordinator()));

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(
            new PlaybackSegmentRunRequest(CreateRequest(), "示例小说 · 第一章", 0, ForceInvalidate: false),
            null,
            cancellation.Token));
    }

    private static AudioGenerationRequest CreateRequest()
    {
        var rule = TestHttpTtsRules.Create(
            1,
            "默认规则",
            "https://example.com/tts?text={{encodeURIComponent(speakText)}}",
            "audio/mpeg",
            null,
            null,
            null,
            null,
            true,
            null,
            "2026-06-24T00:00:00.0000000Z",
            "2026-06-24T00:00:00.0000000Z");
        return new AudioGenerationRequest(
            "book-1",
            0,
            0,
            "第一段",
            rule.Id,
            rule,
            rule.Normalize(),
            10,
            Guid.NewGuid())
        {
            ChapterId = "book-1/chapter/0",
            StableSegmentIdentity = StableSpeechSegmentIdentity.Body(0, 1)
        };
    }

    private sealed class RecordingAudioProvider : IAudioGenerationProvider
    {
        private readonly Queue<AudioGenerationResult> _results = [];

        public List<string> Calls { get; } = [];

        public bool Invalidated { get; private set; }

        public Exception? ExceptionToThrow { get; init; }

        public void Enqueue(AudioGenerationResult result)
        {
            _results.Enqueue(result);
        }

        public Task<AudioGenerationResult> GetAudioAsync(
            AudioGenerationRequest request,
            AudioGenerationPriority priority,
            Action<AudioGenerationProgress>? progressCallback,
            CancellationToken cancellationToken)
        {
            Calls.Add("get");
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(_results.Count == 0
                ? new AudioGenerationResult("generated.mp3", false, null)
                : _results.Dequeue());
        }

        public Task InvalidateAsync(AudioGenerationRequest request, CancellationToken cancellationToken)
        {
            Calls.Add("invalidate");
            Invalidated = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLocalAudioPlaybackCoordinator : ILocalAudioPlaybackCoordinator
    {
        public LocalAudioPlaybackSnapshot CurrentSnapshot { get; private set; } = LocalAudioPlaybackSnapshot.Idle;

        public double Volume { get; private set; } = PlaybackVolume.Default;

        public LocalAudioPlaybackRequest? LastRequest { get; private set; }

        public event EventHandler<LocalAudioPlaybackSnapshot>? SnapshotChanged;

        public event EventHandler? PlaybackCompleted { add { } remove { } }

        public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed { add { } remove { } }

        public Task StartAsync(LocalAudioPlaybackRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            CurrentSnapshot = new LocalAudioPlaybackSnapshot(
                PlaybackState.Playing,
                request.DisplayTitle,
                request.BookId,
                request.ChapterIndex,
                request.SegmentIndex,
                request.ResumePositionMilliseconds,
                1000,
                null,
                request.IsUsingCache);
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SeekAsync(long positionMilliseconds, CancellationToken cancellationToken) => Task.CompletedTask;

        public void SetVolume(double volume)
        {
            Volume = PlaybackVolume.Normalize(volume);
            CurrentSnapshot = CurrentSnapshot with { Volume = this.Volume };
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
