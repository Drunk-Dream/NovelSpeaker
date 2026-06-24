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
    public async Task ExecuteAsync_retries_rate_limited_requests_once()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var result = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "rate-limited")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, server.GetRequestCount("/rate-limited"));
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
                "audio/mpeg",
                TimeSpan.FromSeconds(2)),
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
                "audio/wav",
                TimeSpan.FromSeconds(2)),
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
    public async Task ExecuteAsync_isolates_and_clears_rule_cookies()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var initResult = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "cookie-init")),
            CancellationToken.None);
        var sameRuleResult = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "cookie-required")),
            CancellationToken.None);
        var differentRuleResult = await client.ExecuteAsync(
            CreateRequest(2, new Uri(server.BaseUri, "cookie-required")),
            CancellationToken.None);

        Assert.True(initResult.IsSuccess);
        Assert.True(sameRuleResult.IsSuccess);
        Assert.False(differentRuleResult.IsSuccess);
        Assert.Equal(TtsErrorKind.Unauthorized, differentRuleResult.Failure!.Kind);

        await client.ClearRuleCookiesAsync(1, CancellationToken.None);
        var clearedRuleResult = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "cookie-required")),
            CancellationToken.None);
        Assert.False(clearedRuleResult.IsSuccess);
        Assert.Equal(TtsErrorKind.Unauthorized, clearedRuleResult.Failure!.Kind);
    }

    [Fact]
    public async Task ExecuteAsync_times_out_and_retries_transient_errors()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var timeoutResult = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "slow"), timeout: TimeSpan.FromMilliseconds(50)),
            CancellationToken.None);
        var retryResult = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "server-error")),
            CancellationToken.None);

        Assert.False(timeoutResult.IsSuccess);
        Assert.Equal(TtsErrorKind.Timeout, timeoutResult.Failure!.Kind);
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

    private static HttpTtsClient CreateClient()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        directories.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new HttpTtsClient(directories);
    }

    private static ParsedTtsRequest CreateRequest(
        long ruleId,
        Uri url,
        string method = "GET",
        string? declaredContentType = "audio/wav",
        TimeSpan? timeout = null)
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
            declaredContentType,
            timeout ?? TimeSpan.FromSeconds(2));
    }
}
