using System.Text.Json;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.Infrastructure.Speech.Rules;
using NovelSpeaker.Infrastructure.Speech.Scripting;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class TtsRuleSampleRegressionTests
{
    private readonly LegadoRuleConverter _converter = new();
    private readonly TtsRequestCompiler _compiler = new(new JintTemplateEvaluator());

    [Fact]
    public async Task Sanitized_rule_samples_convert_and_compile()
    {
        var sampleDirectory = Path.Combine(AppContext.BaseDirectory, "TestAssets", "TtsRules");
        var files = Directory.GetFiles(sampleDirectory, "*.json");

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file));
            var conversion = _converter.Convert(document.RootElement);

            Assert.True(conversion.CanImport, $"{Path.GetFileName(file)} should be importable.");

            var rule = conversion.CandidateRule;
            var compilation = await _compiler.CompileAsync(
                rule.ToNormalizedRule(),
                new TtsRuleContext(
                    "sample text",
                    11,
                    rule,
                    new Dictionary<string, string> { ["token"] = "demo-token" }),
                CancellationToken.None);

            Assert.True(compilation.IsSuccess, $"{Path.GetFileName(file)} should compile.");
        }
    }
}
