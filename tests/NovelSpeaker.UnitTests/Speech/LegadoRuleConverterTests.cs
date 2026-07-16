using System.Text.Json;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Infrastructure.Speech.Rules;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class LegadoRuleConverterTests
{
    private readonly LegadoRuleConverter _converter = new();

    [Fact]
    public void Convert_rewrites_supported_compatibility_helpers_and_builds_canonical_rule_json()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "name":"Microsoft Server Speech Text to Speech Voice (zh-CN, XiaoxiaoNeural)",
              "contentType":"audio/mpeg",
              "url":"https://tts.drudream.top/api/text-to-speech?rate={{(speakSpeed - 10) * 2}}&text={{java.encodeURI(speakText)}}",
              "header":"{\"Authorization\":\"Bearer undefined\"}"
            }
            """);

        var result = _converter.Convert(document.RootElement);

        Assert.True(result.CanImport);
        Assert.Equal(TtsRuleCompatibilityStatus.Compatible, result.CompatibilityStatus);
        Assert.Equal(
            "https://tts.drudream.top/api/text-to-speech?rate={{(speakSpeed - 10) * 2}}&text={{encodeURI(speakText)}}",
            result.CandidateRule.Url);
        Assert.Equal(
            """{"name":"Microsoft Server Speech Text to Speech Voice (zh-CN, XiaoxiaoNeural)","url":"https://tts.drudream.top/api/text-to-speech?rate={{(speakSpeed - 10) * 2}}&text={{encodeURI(speakText)}}","contentType":"audio/mpeg","header":"{\"Authorization\":\"Bearer undefined\"}"}""",
            NovelSpeakerRuleJsonSerializer.Serialize(result.CandidateRule));
    }

    [Fact]
    public void Convert_extracts_request_options_from_url_suffix()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "name":"POST JSON Example",
              "url":"https://example.com/tts,{\"method\":\"POST\",\"body\":\"{\\\"text\\\":\\\"{{speakText}}\\\"}\"}"
            }
            """);

        var result = _converter.Convert(document.RootElement);

        Assert.True(result.CanImport);
        Assert.Equal("https://example.com/tts", result.CandidateRule.Url);
        Assert.Equal("POST", result.CandidateRule.RequestMethod);
        Assert.Equal("""{"text":"{{speakText}}"}""", result.CandidateRule.RequestBody);
        Assert.False(result.CandidateRule.RequestBodyIsJsonStructure);
    }

    [Fact]
    public void Convert_blocks_cookie_and_unknown_source_helpers()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "name":"Bad Rule",
              "url":"https://example.com/tts?cookie={{cookie.get('https://example.com')}}&state={{source.get('token')}}"
            }
            """);

        var result = _converter.Convert(document.RootElement);

        Assert.False(result.CanImport);
        Assert.Equal(TtsRuleCompatibilityStatus.NeedsManualAdjustment, result.CompatibilityStatus);
        Assert.NotEmpty(result.BlockingIssues);
    }

    [Theory]
    [InlineData("{\"name\":\"Cookie Jar\",\"url\":\"https://example.com/tts\",\"enabledCookieJar\":true}")]
    [InlineData("{\"name\":\"Login\",\"url\":\"https://example.com/tts\",\"loginInfo\":{\"token\":\"secret\"}}")]
    [InlineData("{\"name\":\"Login template\",\"url\":\"https://example.com/tts?token={{loginInfo.token}}\"}")]
    [InlineData("{\"name\":\"Bare cookie\",\"url\":\"https://example.com/tts?token={{ COOKIE }}\"}")]
    [InlineData("{\"name\":\"Bracket cookie\",\"url\":\"https://example.com/tts?token={{ cookie ['session'] }}\"}")]
    [InlineData("{\"name\":\"Cookie header\",\"url\":\"https://example.com/tts\",\"header\":{\"Cookie\":\"session=secret\"}}")]
    [InlineData("{\"name\":\"Cookie option header\",\"url\":\"https://example.com/tts,{\\\"headers\\\":{\\\"Cookie\\\":\\\"session=secret\\\"}}\"}")]
    public void Convert_blocks_all_cookie_and_login_info_dependencies(string json)
    {
        using var document = JsonDocument.Parse(json);

        var result = _converter.Convert(document.RootElement);

        Assert.False(result.CanImport);
        Assert.Equal(TtsRuleCompatibilityStatus.NeedsManualAdjustment, result.CompatibilityStatus);
        Assert.Contains(result.BlockingIssues, issue =>
            issue.Contains("Cookie/LoginInfo", StringComparison.Ordinal));
        Assert.DoesNotContain(result.BlockingIssues, issue => issue.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Convert_keeps_authorization_header_supported()
    {
        using var document = JsonDocument.Parse(
            """{"name":"Authorization","url":"https://example.com/tts","header":{"Authorization":"Bearer demo"}}""");

        var result = _converter.Convert(document.RootElement);

        Assert.True(result.CanImport);
    }

    [Fact]
    public void Convert_does_not_scan_cookie_or_login_info_in_plain_url_text()
    {
        using var document = JsonDocument.Parse(
            """{"name":"Plain URL","url":"https://example.com/cookie/loginInfo/tts"}""");

        var result = _converter.Convert(document.RootElement);

        Assert.True(result.CanImport);
    }

    [Fact]
    public void Convert_blocks_invalid_template_syntax()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "name":"Broken Rule",
              "url":"https://example.com/tts?text={{speakText}"
            }
            """);

        var result = _converter.Convert(document.RootElement);

        Assert.False(result.CanImport);
        Assert.Contains(result.BlockingIssues, issue => issue.Contains("模板格式无效", StringComparison.Ordinal));
    }
}
