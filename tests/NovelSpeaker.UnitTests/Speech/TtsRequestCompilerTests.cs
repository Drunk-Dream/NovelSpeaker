using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.Infrastructure.Speech.Scripting;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class TtsRequestCompilerTests
{
    private readonly TtsRequestCompiler _compiler = new(new JintTemplateEvaluator());

    [Fact]
    public async Task CompileAsync_builds_get_request_and_redacted_preview()
    {
        var rule = CreateRule(
            "GET",
            "https://example.com/tts?token=super-secret-token&text={{encodeURIComponent(speakText)}}",
            """{"Authorization":"Bearer super-secret-token"}""");
        var context = CreateContext(rule);

        var result = await _compiler.CompileAsync(rule.Normalize(), context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("GET", result.Request!.Method);
        Assert.Equal("https://example.com/tts?token=***&text=***", result.Preview!.Url);
        Assert.Equal("""{"Accept":"*/*","Authorization":"***","User-Agent":"NovelSpeaker/1.0"}""", result.Preview.HeadersJson);
    }

    [Fact]
    public async Task CompileAsync_applies_request_option_headers_and_json_body()
    {
        var rule = CreateRule(
            "POST JSON",
            "https://example.com/tts",
            """{"X-Test":"rule","Content-Type":"application/json"}""",
            """{"method":"POST","body":"{\"text\":\"{{speakText}}\"}"}""");
        var context = CreateContext(rule);

        var result = await _compiler.CompileAsync(rule.Normalize(), context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("POST", result.Request!.Method);
        Assert.Equal("rule", result.Request.Headers["X-Test"]);
        Assert.Equal(ParsedTtsRequestBodyKind.Json, result.Request.Body.Kind);
        Assert.Equal("""{"text":"test"}""", result.Request.Body.RawText);
    }

    [Fact]
    public async Task CompileAsync_parses_form_body_from_string()
    {
        var rule = CreateRule(
            "POST Form",
            "https://example.com/tts",
            """{"Content-Type":"application/x-www-form-urlencoded"}""",
            """{"method":"POST","body":"text={{encodeURIComponent(speakText)}}&speed={{speakSpeed}}"}""");
        var context = CreateContext(rule);

        var result = await _compiler.CompileAsync(rule.Normalize(), context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParsedTtsRequestBodyKind.FormUrlEncoded, result.Request!.Body.Kind);
        Assert.Equal("test", result.Request.Body.FormFields!["text"]);
        Assert.Equal("10", result.Request.Body.FormFields["speed"]);
    }

    [Fact]
    public async Task CompileAsync_uses_only_structured_request_method_and_body()
    {
        var rule = CreateRule(
            "Bad",
            "https://example.com/tts",
            null,
            """{"method":"POST","unknown":true}""");
        var context = CreateContext(rule);

        var result = await _compiler.CompileAsync(rule.Normalize(), context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("POST", result.Request!.Method);
    }

    [Theory]
    [InlineData("https://example.com/tts?token={{loginInfo.token}}", null, null)]
    [InlineData("https://example.com/tts?token={{ cookie }}", null, null)]
    [InlineData("https://example.com/tts?token={{ COOKIE [ 'session' ] }}", null, null)]
    [InlineData("https://example.com/tts", "{\"Cookie\":\"session=secret\"}", null)]
    [InlineData("https://example.com/tts", "{\"X-Token\":\"{{cookie.value}}\"}", null)]
    [InlineData("https://example.com/tts", null, "{\"method\":\"POST\",\"body\":\"{{loginInfo.token}}\"}")]
    public async Task CompileAsync_rejects_cookie_and_login_info_in_legacy_persisted_rules(
        string url,
        string? header,
        string? requestOptionsJson)
    {
        var rule = CreateRule("Legacy", url, header, requestOptionsJson);

        var result = await _compiler.CompileAsync(
            rule.Normalize(),
            CreateContext(rule),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.InvalidRule, result.Failure!.Kind);
        Assert.Contains("Cookie/LoginInfo", result.Failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", result.Failure.Message, StringComparison.Ordinal);
    }

    private static HttpTtsRule CreateRule(
        string name,
        string url,
        string? header = null,
        string? requestOptionsJson = null)
    {
        var utcNow = DateTime.UtcNow.ToString("O");
        return TestHttpTtsRules.Create(
            42,
            name,
            url,
            "audio/wav",
            null,
            header,
            requestOptionsJson,
            null,
            true,
            null,
            utcNow,
            utcNow);
    }

    private static TtsRuleContext CreateContext(HttpTtsRule rule)
    {
        return new TtsRuleContext(
            "test",
            10,
            rule);
    }
}
