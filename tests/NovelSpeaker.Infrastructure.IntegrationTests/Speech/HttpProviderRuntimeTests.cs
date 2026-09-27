using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.Infrastructure.Speech.Scripting;
using NovelSpeaker.TestKit.Speech;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Speech;

public sealed class HttpProviderRuntimeTests
{
    [Fact]
    public async Task Registered_runtime_resolves_and_synthesizes_validated_audio()
    {
        await using var server = new LocalHttpTtsTestServer();
        var provider = CreateProvider(new Uri(server.BaseUri, "audio").ToString());
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        try
        {
            using var services = new ServiceCollection()
                .AddSingleton<IProviderStore>(new Store(provider))
                .AddSingleton<IAppDataDirectoryProvider>(directories)
                .AddSingleton(TimeProvider.System)
                .AddNovelSpeakerSpeechApplication()
                .AddNovelSpeakerSpeechAdapters()
                .BuildServiceProvider();
            var resolution = await services.GetRequiredService<IProviderRuntimeResolver>()
                .ResolveAsync(provider.Id, CancellationToken.None);

            Assert.True(resolution.IsAvailable);
            var result = await resolution.Runtime!.SynthesizeAsync(
                resolution.Provider!, new ProviderSynthesisRequest("试听文本", 10), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal("audio/wav", result.ContentType);
            await using var audio = result.Audio!;
            Assert.True(audio.Length > 0);
            Assert.Equal(1, server.GetRequestCount("/audio"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Compiler_uses_content_type_for_json_form_and_raw_and_rejects_get_body()
    {
        var compiler = new HttpProviderRequestCompiler(new JintTemplateEvaluator());
        var request = new ProviderSynthesisRequest("hello", 10);
        var url = "https://example.com/audio";

        var json = await compiler.CompileAsync(Config(url, "POST", "application/json",
            """{"text":{{JSON.stringify(speakText)}}}"""), request, CancellationToken.None);
        var form = await compiler.CompileAsync(Config(url, "POST", "application/x-www-form-urlencoded",
            "text={{encodeURIComponent(speakText)}}&speed={{speakSpeed}}"), request, CancellationToken.None);
        var raw = await compiler.CompileAsync(Config(url, "POST", null,
            "{{speakText}}"), request, CancellationToken.None);
        var invalidJson = await compiler.CompileAsync(Config(url, "POST", "application/json",
            "{broken"), request, CancellationToken.None);
        var getBody = await compiler.CompileAsync(Config(url, "GET", null,
            "hello"), request, CancellationToken.None);
        var invalidContentType = await compiler.CompileAsync(Config(url, "POST", "broken media type",
            "hello"), request, CancellationToken.None);
        var invalidEvaluatedContentType = await compiler.CompileAsync(
            Config(url, "POST", "{{'broken media type'}}", "hello"), request, CancellationToken.None);

        Assert.True(json.IsSuccess);
        Assert.Equal(ParsedTtsRequestBodyKind.Json, json.Request!.Body.Kind);
        Assert.Equal("""{"text":"hello"}""", json.Request.Body.RawText);
        Assert.Equal(ParsedTtsRequestBodyKind.FormUrlEncoded, form.Request!.Body.Kind);
        Assert.Equal("hello", form.Request.Body.FormFields!["text"]);
        Assert.Equal(ParsedTtsRequestBodyKind.RawText, raw.Request!.Body.Kind);
        Assert.False(invalidJson.IsSuccess);
        Assert.False(getBody.IsSuccess);
        Assert.False(invalidContentType.IsSuccess);
        Assert.False(invalidEvaluatedContentType.IsSuccess);
        Assert.Equal(TtsErrorKind.InvalidRule, invalidContentType.Failure!.Kind);
        Assert.False(ProviderConfigurationValidator.Validate(CreateProvider(url) with
        {
            Configuration = Config(url, "POST", "broken media type", "hello")
        }).IsValid);
    }

    [Fact]
    public async Task Compiler_allows_cookie_but_redacts_preview_and_rejects_injection_and_removed_globals()
    {
        var compiler = new HttpProviderRequestCompiler(new JintTemplateEvaluator());
        var request = new ProviderSynthesisRequest("hello", 10);
        var config = new HttpSpeechProviderConfiguration("https://example.com/audio", "GET",
            new Dictionary<string, string> { ["Cookie"] = "session=secret-token" }, null, null);

        var cookie = await compiler.CompileAsync(config, request, CancellationToken.None);
        var injected = await compiler.CompileAsync(config with
        {
            Headers = new Dictionary<string, string> { ["X-Test"] = "{{'ok\\r\\nX-Evil: yes'}}" }
        }, request, CancellationToken.None);
        var source = await compiler.CompileAsync(config with { UrlTemplate = "https://example.com/{{source.name}}" },
            request, CancellationToken.None);
        var java = await compiler.CompileAsync(config with { UrlTemplate = "https://example.com/{{java.encodeURI(speakText)}}" },
            request, CancellationToken.None);

        Assert.True(cookie.IsSuccess);
        Assert.Equal("session=secret-token", cookie.Request!.Headers["Cookie"]);
        Assert.DoesNotContain("secret-token", cookie.Preview!.HeadersJson);
        Assert.False(injected.IsSuccess);
        Assert.False(source.IsSuccess);
        Assert.False(java.IsSuccess);
    }

    [Fact]
    public async Task Runtime_maps_transport_failure_and_rejects_unconfigured_provider()
    {
        var provider = CreateProvider("https://example.com/audio");
        var runtime = new HttpProviderRuntime(
            new HttpProviderRequestCompiler(new JintTemplateEvaluator()),
            new FailingClient(new TtsExecutionFailure(TtsErrorKind.Network, "网络请求失败。", null, null, null, null)),
            new ProviderRequestLimiter(new TtsRateLimiter(TimeProvider.System)));

        var failed = await runtime.SynthesizeAsync(provider,
            new ProviderSynthesisRequest("hello", 10), CancellationToken.None);
        var unavailable = await runtime.SynthesizeAsync(provider with
        {
            Configuration = ((HttpSpeechProviderConfiguration)provider.Configuration) with { UrlTemplate = "" }
        }, new ProviderSynthesisRequest("hello", 10), CancellationToken.None);

        Assert.Equal(ProviderSynthesisFailureKind.Network, failed.Failure!.Kind);
        Assert.Equal(ProviderSynthesisFailureKind.ProviderUnavailable, unavailable.Failure!.Kind);
    }

    [Fact]
    public async Task Structured_rate_limit_waits_for_configured_window()
    {
        var clock = new ManualTimeProvider();
        var limiter = new ProviderRequestLimiter(new TtsRateLimiter(clock));
        var id = ProviderId.New();
        var policy = new ProviderRequestRateLimit(1, 1000);
        await using (await limiter.AcquireAsync(id, policy, TtsAdmissionPriority.CurrentPlayback, CancellationToken.None))
        {
        }

        var queued = limiter.AcquireAsync(id, policy, TtsAdmissionPriority.ActiveCache, CancellationToken.None);
        await clock.WaitForPendingTimerCountAsync(1);
        Assert.False(queued.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1000));
        await using var second = await queued.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Runtime_releases_audio_when_cancelled_after_execution()
    {
        using var cancellation = new CancellationTokenSource();
        var owner = new CountingAudioOwner();
        var runtime = new HttpProviderRuntime(
            new HttpProviderRequestCompiler(new JintTemplateEvaluator()),
            new CancellingSuccessfulClient(cancellation, owner),
            new ProviderRequestLimiter(new TtsRateLimiter(TimeProvider.System)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.SynthesizeAsync(
            CreateProvider("https://example.com/audio"),
            new ProviderSynthesisRequest("hello", 10), cancellation.Token));
        Assert.Equal(1, owner.DisposeCount);
    }

    private static SpeechProviderInstance CreateProvider(string url) =>
        new(ProviderId.New(), "HTTP Provider", 0, Config(url, "GET", null, null),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static HttpSpeechProviderConfiguration Config(
        string url, string method, string? contentType, string? body) =>
        new(url, method, contentType is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["Content-Type"] = contentType }, body, null);

    private sealed class Store(SpeechProviderInstance provider) : IProviderStore
    {
        public Task<IReadOnlyList<SpeechProviderInstance>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SpeechProviderInstance>>([provider]);

        public Task<SpeechProviderInstance?> GetByIdAsync(ProviderId providerId, CancellationToken cancellationToken) =>
            Task.FromResult(providerId == provider.Id ? provider : null);

        public Task SaveAsync(SpeechProviderInstance value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(ProviderId providerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FailingClient(TtsExecutionFailure failure) : IHttpTtsClient
    {
        public Task<TtsHttpExecutionResult> ExecuteAsync(
            ParsedTtsRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new TtsHttpExecutionResult(null, failure));
    }

    private sealed class CancellingSuccessfulClient(
        CancellationTokenSource cancellation,
        IAsyncDisposable owner) : IHttpTtsClient
    {
        public Task<TtsHttpExecutionResult> ExecuteAsync(
            ParsedTtsRequest request, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromResult(new TtsHttpExecutionResult(
                new TtsAudioResponse("not-opened.wav", 200, "audio/wav", "wav", owner), null));
        }
    }

    private sealed class CountingAudioOwner : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
