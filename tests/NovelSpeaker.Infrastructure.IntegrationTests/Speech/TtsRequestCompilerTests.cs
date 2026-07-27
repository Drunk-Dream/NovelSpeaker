using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Infrastructure.Speech.Scripting;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Speech;

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

    [Fact]
    public async Task CompileAsync_projects_malformed_structured_body_as_safe_invalid_rule()
    {
        var rule = CreateRule("Malformed body", "https://example.com/tts") with
        {
            RequestMethod = "POST",
            RequestBody = "{sensitive-body-fragment",
            RequestBodyIsJsonStructure = true
        };

        var result = await _compiler.CompileAsync(
            rule.Normalize(),
            CreateContext(rule),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.InvalidRule, result.Failure!.Kind);
        Assert.Equal("POST JSON 的 body 不是有效的 JSON。", result.Failure.Message);
        Assert.DoesNotContain("sensitive-body-fragment", result.Failure.Message, StringComparison.Ordinal);
        Assert.Null(result.Preview);
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

    [Fact]
    public async Task CompileAsync_projects_safe_template_failure_and_redacts_diagnostic_log()
    {
        const string token = "fixture-token-9482";
        const string body = "fixture-body-2751";
        const string novelText = "fixture-novel-text-8364";
        var rule = CreateRule(
            "Sensitive",
            $"https://example.com/tts?query={token}&text={{{{speakText}}}}",
            $"{{\"Authorization\":\"Bearer {token}\"}}",
            $"{{\"method\":\"POST\",\"body\":\"{body}\"}}");
        var logger = new CapturingLogger<TtsCompilationFailureReporter>();
        var exceptionText = $"Authorization=Bearer {token}; query={token}; body={body}; text={novelText}";
        var compiler = new TtsRequestCompiler(
            new ThrowingTemplateEvaluator(exceptionText),
            new TtsCompilationFailureReporter(logger));

        var result = await compiler.CompileAsync(
            rule.Normalize(),
            new TtsRuleContext(novelText, 10, rule),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.ScriptError, result.Failure!.Kind);
        Assert.Equal("模板求值失败，请检查规则模板后重试。", result.Failure.Message);
        Assert.Null(result.Preview);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, entry.Level);
        Assert.Null(entry.Exception);
        AssertSensitiveValuesAbsent(entry.Message, token, body, novelText);
        AssertSensitiveValuesAbsent(result.Failure.Message, token, body, novelText);
    }

    [Fact]
    public async Task CompileAsync_does_not_log_user_cancellation_as_error()
    {
        var rule = CreateRule("Cancelled", "https://example.com/{{speakText}}");
        var logger = new CapturingLogger<TtsCompilationFailureReporter>();
        var evaluator = new CancellingTemplateEvaluator();
        var compiler = new TtsRequestCompiler(evaluator, new TtsCompilationFailureReporter(logger));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compiler.CompileAsync(
            rule.Normalize(),
            CreateContext(rule),
            CancellationToken.None));

        Assert.Equal(1, evaluator.CallCount);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error);
    }

    private static void AssertSensitiveValuesAbsent(string value, params string[] sensitiveValues)
    {
        foreach (var sensitiveValue in sensitiveValues)
        {
            Assert.DoesNotContain(sensitiveValue, value, StringComparison.Ordinal);
        }
    }

    private sealed class ThrowingTemplateEvaluator(string message) : ITemplateEvaluator
    {
        public Task<string> EvaluateAsync(
            NormalizedTemplate template,
            TtsRuleContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
    }

    private sealed class CancellingTemplateEvaluator : ITemplateEvaluator
    {
        public int CallCount { get; private set; }

        public Task<string> EvaluateAsync(
            NormalizedTemplate template,
            TtsRuleContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new OperationCanceledException(cancellationToken);
        }
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
