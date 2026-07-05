using NovelSpeaker.Domain.Speech;
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

        var result = await _compiler.CompileAsync(rule.ToNormalizedRule(), context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("GET", result.Request!.Method);
        Assert.Equal("https://example.com/tts?token=***&text=test", result.Preview!.Url);
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

        var result = await _compiler.CompileAsync(rule.ToNormalizedRule(), context, CancellationToken.None);

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

        var result = await _compiler.CompileAsync(rule.ToNormalizedRule(), context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParsedTtsRequestBodyKind.FormUrlEncoded, result.Request!.Body.Kind);
        Assert.Equal("test", result.Request.Body.FormFields!["text"]);
        Assert.Equal("10", result.Request.Body.FormFields["speed"]);
    }

    [Fact]
    public async Task CompileAsync_rejects_unsupported_request_option_fields()
    {
        var rule = CreateRule(
            "Bad",
            "https://example.com/tts",
            null,
            """{"method":"POST","unknown":true}""");
        var context = CreateContext(rule);

        var result = await _compiler.CompileAsync(rule.ToNormalizedRule(), context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.InvalidRule, result.Failure!.Kind);
    }

    private static HttpTtsRule CreateRule(
        string name,
        string url,
        string? header = null,
        string? requestOptionsJson = null)
    {
        var utcNow = DateTime.UtcNow.ToString("O");
        return new HttpTtsRule(
            42,
            name,
            url,
            "audio/wav",
            null,
            header,
            requestOptionsJson,
            null,
            "{}",
            true,
            TtsRuleCompatibilityStatus.Compatible,
            [],
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
