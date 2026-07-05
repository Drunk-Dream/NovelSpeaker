using System.Text.Json;
using NovelSpeaker.Domain.Speech;
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
        Assert.Equal("""{"method":"POST","body":"{\"text\":\"{{speakText}}\"}"}""", result.CandidateRule.RequestOptionsJson);
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
