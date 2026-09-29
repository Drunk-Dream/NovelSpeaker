using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Speech.Providers;
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
        var context = new SpeechTemplateContext(
            "你好 世界",
            12);

        var textResult = await _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{speakText}}|{{speakSpeed}}"),
            context,
            CancellationToken.None);
        var objectResult = await _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{({ text: speakText, speed: speakSpeed })}}"),
            context,
            CancellationToken.None);

        Assert.Equal("你好 世界|12", textResult);
        Assert.Equal("""{"text":"你好 世界","speed":12}""", objectResult);
    }

    [Fact]
    public async Task EvaluateAsync_rejects_infinite_loops_and_untrusted_system_access()
    {
        var context = new SpeechTemplateContext("test", 10);

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
        var context = new SpeechTemplateContext("test", 10);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{'x'.repeat(9000)}}"),
            context,
            CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_supports_base64_functions_without_exposing_CLR()
    {
        var context = new SpeechTemplateContext("hello", 10);
        var encoded = await _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{btoa('ABC')}}"), context, CancellationToken.None);
        var decoded = await _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{atob('QUJD')}}"), context, CancellationToken.None);

        Assert.Equal("QUJD", encoded);
        Assert.Equal("ABC", decoded);
        await Assert.ThrowsAnyAsync<Exception>(() => _evaluator.EvaluateAsync(
            NormalizedTemplate.Parse("{{btoa.GetType()}}"), context, CancellationToken.None));
    }

}
