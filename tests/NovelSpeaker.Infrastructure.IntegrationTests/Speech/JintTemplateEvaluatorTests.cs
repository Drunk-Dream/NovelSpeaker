using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Infrastructure.Speech.Scripting;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Speech;

public sealed class JintTemplateEvaluatorTests
{
    private readonly JintTemplateEvaluator _evaluator = new();

    [Fact]
    public async Task EvaluateAsync_renders_strings_and_serializes_object_results()
    {
        var rule = CreateRule(
            "示例规则",
            "https://example.com/tts?text={{encodeURIComponent(speakText)}}");
        var context = new TtsRuleContext(
            "你好 世界",
            12,
            rule);

        var textResult = await _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{speakText}}|{{speakSpeed}}"),
            context,
            CancellationToken.None);
        var objectResult = await _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{({ text: speakText, speed: speakSpeed, ruleName: source.name })}}"),
            context,
            CancellationToken.None);

        Assert.Equal("你好 世界|12", textResult);
        Assert.Equal("""{"text":"你好 世界","speed":12,"ruleName":"示例规则"}""", objectResult);
    }

    [Fact]
    public async Task EvaluateAsync_rejects_infinite_loops_and_untrusted_system_access()
    {
        var rule = CreateRule("安全规则", "https://example.com/tts");
        var context = new TtsRuleContext("test", 10, rule);

        await Assert.ThrowsAnyAsync<Exception>(() => _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{(() => { while (true) {} })()}}"),
            context,
            CancellationToken.None));

        await Assert.ThrowsAnyAsync<Exception>(() => _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{System.IO.File.ReadAllText('test.txt')}}"),
            context,
            CancellationToken.None));

        await Assert.ThrowsAnyAsync<Exception>(() => _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{process.start('cmd.exe')}}"),
            context,
            CancellationToken.None));

        await Assert.ThrowsAnyAsync<Exception>(() => _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{typeof(System.Reflection.Assembly)}}"),
            context,
            CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_rejects_excessive_output()
    {
        var rule = CreateRule("安全规则", "https://example.com/tts");
        var context = new TtsRuleContext("test", 10, rule);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{'x'.repeat(9000)}}"),
            context,
            CancellationToken.None));
    }

    private static HttpTtsRule CreateRule(
        string name,
        string url,
        string? header = null,
        string? requestOptionsJson = null)
    {
        var utcNow = DateTime.UtcNow.ToString("O");
        return TestHttpTtsRules.Create(
            1,
            name,
            url,
            "audio/mpeg",
            null,
            header,
            requestOptionsJson,
            null,
            true,
            null,
            utcNow,
            utcNow);
    }
}
