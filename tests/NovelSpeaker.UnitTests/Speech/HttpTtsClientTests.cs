using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Speech.Http;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class HttpTtsClientTests
{
    [Fact]
    public async Task ExecuteAsync_downloads_and_validates_audio()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var result = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "audio")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Audio);
        Assert.True(File.Exists(result.Audio!.FilePath));
    }

    [Fact]
    public async Task ExecuteAsync_returns_retry_after_for_rate_limited_requests()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var result = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "rate-limited")),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.RateLimited, result.Failure!.Kind);
        Assert.Equal(TimeSpan.Zero, result.Failure.RetryAfter);
        Assert.Equal(1, server.GetRequestCount("/rate-limited"));
    }

    [Fact]
    public async Task ExecuteAsync_sends_post_json_and_form_payloads()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var jsonResult = await client.ExecuteAsync(
            new ParsedTtsRequest(
                10,
                "POST",
                new Uri(server.BaseUri, "audio-json"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Accept"] = "*/*",
                    ["User-Agent"] = "NovelSpeaker.Tests",
                    ["Content-Type"] = "application/json"
                },
                new ParsedTtsRequestBody(ParsedTtsRequestBodyKind.Json, """{"text":"demo"}""", null),
                "audio/mpeg"),
            CancellationToken.None);
        var formResult = await client.ExecuteAsync(
            new ParsedTtsRequest(
                11,
                "POST",
                new Uri(server.BaseUri, "audio-form"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Accept"] = "*/*",
                    ["User-Agent"] = "NovelSpeaker.Tests",
                    ["Content-Type"] = "application/x-www-form-urlencoded"
                },
                new ParsedTtsRequestBody(
                    ParsedTtsRequestBodyKind.FormUrlEncoded,
                    "text=demo&speed=10",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["text"] = "demo",
                        ["speed"] = "10"
                    }),
                "audio/wav"),
            CancellationToken.None);

        Assert.True(jsonResult.IsSuccess);
        Assert.True(formResult.IsSuccess);
        Assert.Equal("""{"text":"demo"}""", server.LastJsonBody);
        Assert.NotNull(server.LastFormBody);
        Assert.Contains("text=demo", server.LastFormBody);
        Assert.Contains("speed=10", server.LastFormBody);
    }

    [Fact]
    public async Task ExecuteAsync_classifies_json_error_and_redacts_summary()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var result = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "error-json")),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.InvalidResponse, result.Failure!.Kind);
        Assert.DoesNotContain("super-secret", result.Failure.ResponseSummary);
    }

    [Fact]
    public async Task ExecuteAsync_times_out_when_request_exceeds_timeout()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient(TimeSpan.FromMilliseconds(50));

        var timeoutResult = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "slow")),
            CancellationToken.None);

        Assert.False(timeoutResult.IsSuccess);
        Assert.Equal(TtsErrorKind.Timeout, timeoutResult.Failure!.Kind);
    }

    [Fact]
    public async Task ExecuteAsync_retries_transient_server_errors()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var retryResult = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "server-error")),
            CancellationToken.None);

        Assert.True(retryResult.IsSuccess);
        Assert.Equal(3, server.GetRequestCount("/server-error"));
    }

    [Fact]
    public async Task ExecuteAsync_rejects_corrupt_audio()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var result = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "corrupt-audio"), declaredContentType: "audio/mpeg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.AudioDecode, result.Failure!.Kind);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_cookie_header_before_sending_request()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();
        var request = CreateRequest(1, new Uri(server.BaseUri, "cookie-required")) with
        {
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cookie"] = "session=rule-cookie"
            }
        };

        var result = await client.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.InvalidRule, result.Failure!.Kind);
        Assert.Contains("Cookie/LoginInfo", result.Failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, server.GetRequestCount("/cookie-required"));
    }

    private static HttpTtsClient CreateClient(TimeSpan? requestTimeout = null)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        directories.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new HttpTtsClient(directories, requestTimeout: requestTimeout);
    }

    private static ParsedTtsRequest CreateRequest(
        long ruleId,
        Uri url,
        string method = "GET",
        string? declaredContentType = "audio/wav")
    {
        return new ParsedTtsRequest(
            ruleId,
            method,
            url,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Accept"] = "*/*",
                ["User-Agent"] = "NovelSpeaker.Tests"
            },
            ParsedTtsRequestBody.None,
            declaredContentType);
    }
}
