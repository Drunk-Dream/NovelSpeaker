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
    public async Task Supported_rule_samples_convert_and_compile()
    {
        var sampleDirectory = Path.Combine(AppContext.BaseDirectory, "TestAssets", "TtsRules");
        var files = new[]
        {
            "get-sample.json",
            "header-sample.json",
            "post-form-sample.json",
            "post-json-sample.json"
        };

        Assert.NotEmpty(files);

        foreach (var fileName in files)
        {
            var file = Path.Combine(sampleDirectory, fileName);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file));
            var conversion = _converter.Convert(document.RootElement);

            Assert.True(conversion.CanImport, $"{Path.GetFileName(file)} should be importable.");

            var rule = conversion.CandidateRule;
            var compilation = await _compiler.CompileAsync(
                rule.ToNormalizedRule(),
                new TtsRuleContext(
                    "sample text",
                    11,
                    rule),
                CancellationToken.None);

            Assert.True(compilation.IsSuccess, $"{Path.GetFileName(file)} should compile.");
        }
    }

    [Theory]
    [InlineData("cookie-sample.json")]
    [InlineData("login-info-sample.json")]
    public async Task Unsupported_cookie_and_login_info_samples_are_blocked(string fileName)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "TestAssets", "TtsRules", fileName);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file));

        var conversion = _converter.Convert(document.RootElement);

        Assert.False(conversion.CanImport);
        Assert.Contains(conversion.BlockingIssues, issue =>
            issue.Contains("Cookie/LoginInfo", StringComparison.Ordinal));
    }
}
