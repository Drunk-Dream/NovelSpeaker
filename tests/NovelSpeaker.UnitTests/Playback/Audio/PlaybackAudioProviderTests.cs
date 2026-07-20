using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Audio;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.UnitTests.Common;
using Microsoft.Extensions.Logging;
using NovelSpeaker.UnitTests.Speech;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback.Audio;

public sealed class PlaybackAudioProviderTests
{
    [Fact]
    public async Task GetAudioAsync_returns_cached_file_without_calling_tts_pipeline()
    {
        var request = CreatePlaybackRequest();
        var cache = new FakeAudioCache
        {
            EntryToReturn = new AudioCacheEntry(AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段"), "cached.mp3")
        };
        var compiler = new FakeTtsRequestCompiler();
        var httpClient = new FakeHttpTtsClient();
        var rateLimiter = new CountingRateLimiter();
        var provider = new PlaybackAudioProvider(compiler, httpClient, cache, rateLimiter);

        var result = await provider.GetAudioAsync(
            request,
            PlaybackAudioPriority.Current,
            null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsUsingCache);
        Assert.Equal("cached.mp3", result.FilePath);
        Assert.Equal(0, compiler.CallCount);
        Assert.Equal(0, httpClient.ExecuteCallCount);
        Assert.Equal(0, rateLimiter.WaitCallCount);
    }

    [Fact]
    public async Task GetAudioAsync_deduplicates_same_cache_key_between_prefetch_and_current()
    {
        var request = CreatePlaybackRequest();
        var compiler = new FakeTtsRequestCompiler
        {
            CompilationResult = CreateSuccessfulCompilationResult()
        };
        var httpClient = new FakeHttpTtsClient();
        var pending = httpClient.EnqueuePendingSuccess();
        var provider = new PlaybackAudioProvider(
            compiler,
            httpClient,
            new FakeAudioCache(),
            new CountingRateLimiter());

        var prefetchTask = provider.GetAudioAsync(
            request,
            PlaybackAudioPriority.Prefetch,
            null,
            CancellationToken.None);
        await pending.ExecutionStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var currentTask = provider.GetAudioAsync(
            request,
            PlaybackAudioPriority.Current,
            null,
            CancellationToken.None);

        pending.CompleteSuccess();

        var prefetchResult = await prefetchTask;
        var currentResult = await currentTask;

        Assert.True(prefetchResult.IsSuccess);
        Assert.True(currentResult.IsSuccess);
        Assert.Equal(1, httpClient.ExecuteCallCount);
    }

    [Fact]
    public async Task GetAudioAsync_current_request_preempts_different_prefetch_request()
    {
        var firstRequest = CreatePlaybackRequest(segmentIndex: 0, speechText: "第一段");
        var secondRequest = CreatePlaybackRequest(segmentIndex: 1, speechText: "第二段");
        var httpClient = new FakeHttpTtsClient();
        var cancelledExecution = httpClient.EnqueuePendingSuccess();
        httpClient.EnqueueSuccess();
        var provider = new PlaybackAudioProvider(
            new FakeTtsRequestCompiler { CompilationResult = CreateSuccessfulCompilationResult() },
            httpClient,
            new FakeAudioCache(),
            new CountingRateLimiter());

        var prefetchTask = provider.GetAudioAsync(
            firstRequest,
            PlaybackAudioPriority.Prefetch,
            null,
            CancellationToken.None);
        await cancelledExecution.ExecutionStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var currentTask = provider.GetAudioAsync(
            secondRequest,
            PlaybackAudioPriority.Current,
            null,
            CancellationToken.None);

        await cancelledExecution.CancellationRequested.WaitAsync(TimeSpan.FromSeconds(5));
        var currentResult = await currentTask;
        var prefetchResult = await prefetchTask;

        Assert.True(currentResult.IsSuccess);
        Assert.False(prefetchResult.IsSuccess);
        Assert.Equal(TtsErrorKind.Cancelled, prefetchResult.Failure!.Kind);
        Assert.Equal(2, httpClient.ExecuteCallCount);
    }

    [Fact]
    public async Task GetAudioAsync_retries_429_with_retry_after_for_current_segment()
    {
        var timeProvider = new ManualTimeProvider();
        var request = CreatePlaybackRequest();
        var httpClient = new FakeHttpTtsClient();
        httpClient.EnqueueRateLimited(TimeSpan.FromSeconds(2));
        httpClient.EnqueueSuccess();
        var provider = new PlaybackAudioProvider(
            new FakeTtsRequestCompiler { CompilationResult = CreateSuccessfulCompilationResult() },
            httpClient,
            new FakeAudioCache(),
            new TtsRateLimiter(timeProvider));
        var progressMessages = new List<string>();

        var resultTask = provider.GetAudioAsync(
            request,
            PlaybackAudioPriority.Current,
            progress => progressMessages.Add(progress.Message),
            CancellationToken.None);

        await httpClient.FirstExecutionStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await AssertPendingAsync(resultTask);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var result = await resultTask;

        Assert.True(result.IsSuccess);
        Assert.Contains(progressMessages, message => message.Contains("正在等待", StringComparison.Ordinal));
        Assert.Equal(2, httpClient.ExecuteCallCount);
    }

    [Fact]
    public async Task GetAudioAsync_returns_invalid_rule_when_concurrent_rate_is_invalid()
    {
        var rule = CreateRule(concurrentRate: "not-a-rate");
        var request = new PlaybackAudioRequest(
            "book-1",
            0,
            0,
            "第一段",
            rule.Id,
            rule,
            rule.Normalize(),
            10,
            Guid.NewGuid());
        var httpClient = new FakeHttpTtsClient();
        var provider = new PlaybackAudioProvider(
            new FakeTtsRequestCompiler { CompilationResult = CreateSuccessfulCompilationResult() },
            httpClient,
            new FakeAudioCache(),
            new TtsRateLimiter(TimeProvider.System));

        var result = await provider.GetAudioAsync(
            request,
            PlaybackAudioPriority.Current,
            null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.InvalidRule, result.Failure!.Kind);
        Assert.Equal(0, httpClient.ExecuteCallCount);
    }

    [Fact]
    public async Task InvalidateAsync_uses_the_same_cache_identity_as_get_audio()
    {
        var request = CreatePlaybackRequest();
        var cache = new FakeAudioCache();
        var provider = new PlaybackAudioProvider(
            new FakeTtsRequestCompiler(),
            new FakeHttpTtsClient(),
            cache,
            new CountingRateLimiter());

        await provider.InvalidateAsync(request, CancellationToken.None);

        Assert.Equal(AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段"), cache.InvalidatedKey);
    }

    [Fact]
    public async Task GetAudioAsync_passes_source_rule_into_request_context()
    {
        var rule = CreateRule();
        var request = new PlaybackAudioRequest(
            "book-1",
            0,
            0,
            "第一段",
            rule.Id,
            rule,
            rule.Normalize(),
            10,
            Guid.NewGuid());
        var compiler = new FakeTtsRequestCompiler
        {
            CompilationResult = CreateSuccessfulCompilationResult()
        };
        var provider = new PlaybackAudioProvider(
            compiler,
            new FakeHttpTtsClient(),
            new FakeAudioCache(),
            new CountingRateLimiter());

        await provider.GetAudioAsync(
            request,
            PlaybackAudioPriority.Current,
            null,
            CancellationToken.None);

        Assert.Equal("默认规则", compiler.LastContext!.Source.Name);
    }

    [Fact]
    public async Task GetAudioAsync_projects_safe_unexpected_failure_and_redacts_log()
    {
        const string token = "fixture-token-4371";
        const string novelText = "fixture-novel-text-5982";
        var rule = CreateRule() with
        {
            Url = $"https://example.com/tts?token={token}",
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
        };
        var request = new PlaybackAudioRequest(
            "book-1",
            0,
            0,
            novelText,
            rule.Id,
            rule,
            rule.Normalize(),
            10,
            Guid.NewGuid());
        var logger = new CapturingLogger<PlaybackAudioFailureReporter>();
        var provider = new PlaybackAudioProvider(
            new FakeTtsRequestCompiler
            {
                ExceptionToThrow = new InvalidOperationException(
                    $"Authorization=Bearer {token}; query={token}; text={novelText}")
            },
            new FakeHttpTtsClient(),
            new FakeAudioCache(),
            new CountingRateLimiter(),
            new PlaybackAudioFailureReporter(logger));

        var result = await provider.GetAudioAsync(
            request,
            PlaybackAudioPriority.Current,
            null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.Unknown, result.Failure!.Kind);
        Assert.Equal("音频生成失败，请稍后重试。", result.Failure.Message);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(token, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(novelText, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, result.Failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(novelText, result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAudioAsync_projects_safe_initial_cache_failure()
    {
        const string novelText = "fixture-novel-text-2195";
        var request = CreatePlaybackRequest(speechText: novelText);
        var logger = new CapturingLogger<PlaybackAudioFailureReporter>();
        var provider = new PlaybackAudioProvider(
            new FakeTtsRequestCompiler(),
            new FakeHttpTtsClient(),
            new FakeAudioCache
            {
                LookupException = new IOException($"cache path contains {novelText}")
            },
            new CountingRateLimiter(),
            new PlaybackAudioFailureReporter(logger));

        var result = await provider.GetAudioAsync(
            request,
            PlaybackAudioPriority.Current,
            null,
            CancellationToken.None);

        Assert.Equal(TtsErrorKind.Unknown, result.Failure!.Kind);
        Assert.Equal("音频生成失败，请稍后重试。", result.Failure.Message);
        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(novelText, entry.Message, StringComparison.Ordinal);
    }

    private static PlaybackAudioRequest CreatePlaybackRequest(
        int segmentIndex = 0,
        string speechText = "第一段")
    {
        var rule = CreateRule();
        return new PlaybackAudioRequest(
            "book-1",
            0,
            segmentIndex,
            speechText,
            rule.Id,
            rule,
            rule.Normalize(),
            10,
            Guid.NewGuid());
    }

    private static HttpTtsRule CreateRule(string? concurrentRate = null)
    {
        return TestHttpTtsRules.Create(
            1,
            "默认规则",
            "https://example.com/tts?text={{encodeURIComponent(speakText)}}&speed={{speakSpeed}}",
            "audio/mpeg",
            concurrentRate,
            null,
            null,
            null,
            true,
            null,
            "2026-06-24T00:00:00.0000000Z",
            "2026-06-24T00:00:00.0000000Z");
    }

    private static TtsRequestCompilationResult CreateSuccessfulCompilationResult()
    {
        return new TtsRequestCompilationResult(
            new ParsedTtsRequest(
                1,
                "GET",
                new Uri("https://example.com/tts"),
                new Dictionary<string, string>(),
                ParsedTtsRequestBody.None,
                "audio/mpeg"),
            new TtsRequestPreview("GET", "https://example.com/tts", null, null, "audio/mpeg"),
            [],
            null);
    }

    private static string CopyAudioToTempFile(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}{extension}");
        File.Copy(sourcePath, tempPath, overwrite: true);
        return tempPath;
    }

    private static async Task AssertPendingAsync(Task task)
    {
        await Task.Yield();
        Assert.False(task.IsCompleted);
    }

    private sealed class FakeAudioCache : IAudioCache
    {
        public AudioCacheEntry? EntryToReturn { get; set; }

        public Exception? LookupException { get; init; }

        public AudioCacheWriteRequest? StoredRequest { get; private set; }

        public AudioCacheEntry? StoredResult { get; private set; }

        public AudioCacheKey? InvalidatedKey { get; private set; }

        public Task<AudioCacheEntry?> TryGetAsync(AudioCacheKey key, CancellationToken cancellationToken)
        {
            if (LookupException is not null)
            {
                throw LookupException;
            }

            return Task.FromResult(EntryToReturn);
        }

        public Task<AudioCacheEntry> StoreAsync(AudioCacheWriteRequest request, CancellationToken cancellationToken)
        {
            StoredRequest = request;
            StoredResult = new AudioCacheEntry(request.Key, $"stored-{Path.GetFileName(request.SourceFilePath)}");
            return Task.FromResult(StoredResult);
        }

        public Task InvalidateAsync(AudioCacheKey key, CancellationToken cancellationToken)
        {
            InvalidatedKey = key;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTtsRequestCompiler : ITtsRequestCompiler
    {
        public int CallCount { get; private set; }

        public TtsRuleContext? LastContext { get; private set; }

        public TtsRequestCompilationResult CompilationResult { get; set; } =
            CreateSuccessfulCompilationResult();

        public Exception? ExceptionToThrow { get; init; }

        public Task<TtsRequestCompilationResult> CompileAsync(
            NormalizedHttpTtsRule rule,
            TtsRuleContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastContext = context;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(CompilationResult);
        }
    }

    private sealed class CountingRateLimiter : ITtsRateLimiter
    {
        public int WaitCallCount { get; private set; }

        public int RetryAfterCallCount { get; private set; }

        public Task WaitAsync(long ruleId, string? concurrentRate, CancellationToken cancellationToken)
        {
            WaitCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void ApplyRetryAfter(long ruleId, TimeSpan retryAfter)
        {
            RetryAfterCallCount++;
        }
    }

    private sealed class FakeHttpTtsClient : IHttpTtsClient
    {
        private readonly Queue<Func<CancellationToken, Task<TtsHttpExecutionResult>>> _results = [];
        private readonly TaskCompletionSource _firstExecutionStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ExecuteCallCount { get; private set; }

        public Task FirstExecutionStarted => _firstExecutionStarted.Task;

        public PendingHttpResult EnqueuePendingSuccess()
        {
            var completionSource = new TaskCompletionSource<TtsHttpExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new PendingHttpResult(completionSource);
            _results.Enqueue(cancellationToken =>
            {
                pending.CancellationToken = cancellationToken;
                pending.MarkExecutionStarted();
                cancellationToken.Register(static state => ((PendingHttpResult)state!).MarkCancelled(), pending);
                return completionSource.Task;
            });
            return pending;
        }

        public void EnqueueSuccess()
        {
            _results.Enqueue(_ => Task.FromResult(new TtsHttpExecutionResult(
                new TtsAudioResponse(CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), 200, "audio/mpeg", "mp3"),
                null)));
        }

        public void EnqueueRateLimited(TimeSpan retryAfter)
        {
            _results.Enqueue(_ => Task.FromResult(new TtsHttpExecutionResult(
                null,
                new TtsExecutionFailure(
                    TtsErrorKind.RateLimited,
                    "请求过于频繁，服务暂时限流。",
                    429,
                    "slow down",
                    "text/plain",
                    retryAfter))));
        }

        public Task<TtsHttpExecutionResult> ExecuteAsync(ParsedTtsRequest request, CancellationToken cancellationToken)
        {
            ExecuteCallCount++;
            _firstExecutionStarted.TrySetResult();
            if (_results.Count > 0)
            {
                return _results.Dequeue().Invoke(cancellationToken);
            }

            return Task.FromResult(new TtsHttpExecutionResult(
                new TtsAudioResponse(CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), 200, "audio/mpeg", "mp3"),
                null));
        }

        public sealed class PendingHttpResult
        {
            private readonly TaskCompletionSource<TtsHttpExecutionResult> _completionSource;
            private readonly TaskCompletionSource _executionStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _cancellationRequested =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public PendingHttpResult(TaskCompletionSource<TtsHttpExecutionResult> completionSource)
            {
                _completionSource = completionSource;
            }

            public CancellationToken CancellationToken { get; set; }

            public Task ExecutionStarted => _executionStarted.Task;

            public Task CancellationRequested => _cancellationRequested.Task;

            public void MarkExecutionStarted()
            {
                _executionStarted.TrySetResult();
            }

            public void CompleteSuccess()
            {
                _completionSource.TrySetResult(new TtsHttpExecutionResult(
                    new TtsAudioResponse(CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), 200, "audio/mpeg", "mp3"),
                    null));
            }

            public void MarkCancelled()
            {
                _cancellationRequested.TrySetResult();
                _completionSource.TrySetCanceled(CancellationToken);
            }
        }
    }
}
