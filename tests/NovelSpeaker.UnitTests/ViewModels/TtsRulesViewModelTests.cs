using NovelSpeaker.Application.Speech;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Speech;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class TtsRulesViewModelTests
{
    [Fact]
    public async Task ImportJsonTextAsync_prepares_preview_and_waits_for_confirmation()
    {
        var service = new FakeTtsRuleLibraryService(
            [],
            new TtsRuleImportPreview(
                "剪贴板",
                [
                    new TtsRuleImportItem(
                        0,
                        "示例规则",
                        "https://example.com/tts",
                        TtsRuleCompatibilityStatus.Compatible,
                        [],
                        true,
                        false,
                        false,
                        "可直接导入。",
                        CreateRule("示例规则", "https://example.com/tts"))
                ],
                null));
        var viewModel = new TtsRulesViewModel(service);

        await viewModel.ImportJsonTextAsync("""{"name":"示例规则","url":"https://example.com/tts"}""", "剪贴板", CancellationToken.None);

        Assert.True(viewModel.IsPreviewVisible);
        Assert.Single(viewModel.PreviewItems);
        Assert.Equal("请确认本次规则导入。", viewModel.StatusMessage);
        Assert.Equal(0, service.ImportCallCount);
    }

    [Fact]
    public async Task ConfirmImportAsync_refreshes_rule_list_and_updates_status()
    {
        var service = new FakeTtsRuleLibraryService(
            [
                new TtsRuleSummary(1, "现有规则", true, true, "now", TtsRuleCompatibilityStatus.Compatible, [])
            ],
            new TtsRuleImportPreview(
                "file.json",
                [
                    new TtsRuleImportItem(
                        0,
                        "现有规则",
                        "https://example.com/tts",
                        TtsRuleCompatibilityStatus.Compatible,
                        [],
                        true,
                        false,
                        false,
                        "可直接导入。",
                        CreateRule("现有规则", "https://example.com/tts"))
                ],
                null));
        var viewModel = new TtsRulesViewModel(service);
        await viewModel.ImportJsonTextAsync("""{"name":"现有规则","url":"https://example.com/tts"}""", "file.json", CancellationToken.None);

        await viewModel.ConfirmImportCommand.ExecuteAsync(null);

        Assert.Equal(1, service.ImportCallCount);
        Assert.False(viewModel.IsPreviewVisible);
        Assert.Single(viewModel.Rules);
        Assert.Equal("当前规则：现有规则", viewModel.CurrentRuleDisplayText);
    }

    private static HttpTtsRule CreateRule(string name, string url)
    {
        var utcNow = DateTime.UtcNow.ToString("O");
        return new HttpTtsRule(
            0,
            name,
            url,
            null,
            null,
            null,
            null,
            false,
            null,
            $$"""{"name":"{{name}}","url":"{{url}}"}""",
            true,
            TtsRuleCompatibilityStatus.Compatible,
            [],
            null,
            utcNow,
            utcNow);
    }

    private sealed class FakeTtsRuleLibraryService : ITtsRuleLibraryService
    {
        private readonly IReadOnlyList<TtsRuleSummary> _rules;
        private readonly TtsRuleImportPreview _preview;

        public FakeTtsRuleLibraryService(IReadOnlyList<TtsRuleSummary> rules, TtsRuleImportPreview preview)
        {
            _rules = rules;
            _preview = preview;
        }

        public int ImportCallCount { get; private set; }

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_rules);
        }

        public Task<TtsRuleImportPreview> CreateImportPreviewAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken)
        {
            return Task.FromResult(_preview);
        }

        public Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken)
        {
            ImportCallCount++;
            return Task.FromResult(new TtsRuleImportResult(1, 0, preview.Items.Count));
        }

        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
