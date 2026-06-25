using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

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
        var provider = new PlaybackAudioProvider(compiler, httpClient, cache);

        var result = await provider.GetAudioAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsUsingCache);
        Assert.Equal("cached.mp3", result.FilePath);
        Assert.Equal(0, compiler.CallCount);
        Assert.Equal(0, httpClient.ExecuteCallCount);
    }

    [Fact]
    public async Task GetAudioAsync_executes_http_tts_and_stores_audio_on_cache_miss()
    {
        var request = CreatePlaybackRequest();
        var compiler = new FakeTtsRequestCompiler
        {
            CompilationResult = CreateSuccessfulCompilationResult()
        };
        var httpClient = new FakeHttpTtsClient
        {
            ExecutionResult = new TtsHttpExecutionResult(
                new TtsAudioResponse(CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), 200, "audio/mpeg", "mp3"),
                null)
        };
        var cache = new FakeAudioCache();
        var provider = new PlaybackAudioProvider(compiler, httpClient, cache);

        var result = await provider.GetAudioAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsUsingCache);
        Assert.NotNull(cache.StoredRequest);
        Assert.Equal(request.BookId, cache.StoredRequest!.BookId);
        Assert.Equal(request.ChapterIndex, cache.StoredRequest.ChapterIndex);
        Assert.Equal(request.SegmentIndex, cache.StoredRequest.SegmentIndex);
        Assert.Equal(request.RuleId, cache.StoredRequest.RuleId);
        Assert.Equal("audio/mpeg", cache.StoredRequest.ContentType);
        Assert.Equal(cache.StoredResult!.FilePath, result.FilePath);
    }

    [Fact]
    public async Task InvalidateAsync_uses_the_same_cache_identity_as_get_audio()
    {
        var request = CreatePlaybackRequest();
        var cache = new FakeAudioCache();
        var provider = new PlaybackAudioProvider(new FakeTtsRequestCompiler(), new FakeHttpTtsClient(), cache);

        await provider.InvalidateAsync(request, CancellationToken.None);

        Assert.Equal(AudioCacheKey.FromPlayback("book-1", 0, 0, 1, 10, "第一段"), cache.InvalidatedKey);
    }

    private static PlaybackAudioRequest CreatePlaybackRequest()
    {
        var rule = CreateRule();
        return new PlaybackAudioRequest(
            "book-1",
            0,
            0,
            "第一段",
            rule.Id,
            rule,
            rule.ToNormalizedRule(),
            10,
            Guid.NewGuid());
    }

    private static HttpTtsRule CreateRule()
    {
        return new HttpTtsRule(
            1,
            "默认规则",
            "https://example.com/tts?text={{encodeURIComponent(speakText)}}&speed={{speakSpeed}}",
            "audio/mpeg",
            null,
            null,
            null,
            false,
            null,
            """{"name":"默认规则"}""",
            true,
            TtsRuleCompatibilityStatus.Compatible,
            [],
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
                "audio/mpeg",
                TimeSpan.FromSeconds(5)),
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

    private sealed class FakeAudioCache : IAudioCache
    {
        public AudioCacheEntry? EntryToReturn { get; set; }

        public AudioCacheWriteRequest? StoredRequest { get; private set; }

        public AudioCacheEntry? StoredResult { get; private set; }

        public AudioCacheKey? InvalidatedKey { get; private set; }

        public Task<AudioCacheEntry?> TryGetAsync(AudioCacheKey key, CancellationToken cancellationToken)
        {
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

        public TtsRequestCompilationResult CompilationResult { get; set; } =
            CreateSuccessfulCompilationResult();

        public Task<TtsRequestCompilationResult> CompileAsync(
            NormalizedHttpTtsRule rule,
            TtsRuleContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(CompilationResult);
        }
    }

    private sealed class FakeHttpTtsClient : IHttpTtsClient
    {
        public int ExecuteCallCount { get; private set; }

        public TtsHttpExecutionResult ExecutionResult { get; set; } =
            new(new TtsAudioResponse(CopyAudioToTempFile(PlaybackTestAudio.DemoMp3Path), 200, "audio/mpeg", "mp3"), null);

        public Task<TtsHttpExecutionResult> ExecuteAsync(ParsedTtsRequest request, CancellationToken cancellationToken)
        {
            ExecuteCallCount++;
            return Task.FromResult(ExecutionResult);
        }

        public Task ClearRuleCookiesAsync(long ruleId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
