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
        var libraryService = new FakeTtsRuleLibraryService(
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
        var viewModel = new TtsRulesViewModel(libraryService, new FakeTtsRuleTestService());

        await viewModel.ImportJsonTextAsync("""{"name":"示例规则","url":"https://example.com/tts"}""", "剪贴板", CancellationToken.None);

        Assert.True(viewModel.IsPreviewVisible);
        Assert.Single(viewModel.PreviewItems);
        Assert.Equal("请确认本次规则导入。", viewModel.StatusMessage);
        Assert.Equal(0, libraryService.ImportCallCount);
    }

    [Fact]
    public async Task ConfirmImportAsync_refreshes_rule_list_and_updates_status()
    {
        var libraryService = new FakeTtsRuleLibraryService(
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
        var viewModel = new TtsRulesViewModel(libraryService, new FakeTtsRuleTestService());
        await viewModel.ImportJsonTextAsync("""{"name":"现有规则","url":"https://example.com/tts"}""", "file.json", CancellationToken.None);

        await viewModel.ConfirmImportCommand.ExecuteAsync(null);

        Assert.Equal(1, libraryService.ImportCallCount);
        Assert.False(viewModel.IsPreviewVisible);
        Assert.Single(viewModel.Rules);
        Assert.Equal("当前规则：现有规则", viewModel.CurrentRuleDisplayText);
    }

    [Fact]
    public async Task GeneratePreviewAsync_projects_preview_and_redacts_details()
    {
        var ruleSummary = new TtsRuleSummary(3, "测试规则", true, false, null, TtsRuleCompatibilityStatus.Compatible, []);
        var viewModel = new TtsRulesViewModel(
            new FakeTtsRuleLibraryService([ruleSummary], new TtsRuleImportPreview("x", [], null)),
            new FakeTtsRuleTestService
            {
                PreviewResult = new TtsRuleTestPreviewResult(
                    true,
                    "已生成请求预览。",
                    new TtsRequestPreview(
                        "POST",
                        "https://example.com/tts?token=***",
                        """{"Authorization":"***"}""",
                        """{"text":"demo"}""",
                        "audio/wav"),
                    [],
                    null)
            });

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedRule = ruleSummary;
        await viewModel.GeneratePreviewCommand.ExecuteAsync(null);

        Assert.Equal("POST", viewModel.PreviewMethodText);
        Assert.Equal("https://example.com/tts?token=***", viewModel.PreviewUrlText);
        Assert.Equal("""{"Authorization":"***"}""", viewModel.PreviewHeadersText);
        Assert.Equal("""{"text":"demo"}""", viewModel.PreviewBodyText);
    }

    [Fact]
    public async Task TestSelectedRuleAsync_projects_failure_status_and_response_details()
    {
        var ruleSummary = new TtsRuleSummary(4, "失败规则", true, false, null, TtsRuleCompatibilityStatus.Compatible, []);
        var viewModel = new TtsRulesViewModel(
            new FakeTtsRuleLibraryService([ruleSummary], new TtsRuleImportPreview("x", [], null)),
            new FakeTtsRuleTestService
            {
                TestResult = new TtsRuleTestResult(
                    false,
                    "请求过于频繁，服务暂时限流。",
                    new TtsRequestPreview(
                        "GET",
                        "https://example.com/tts",
                        null,
                        null,
                        "audio/wav"),
                    [],
                    TtsErrorKind.RateLimited,
                    429,
                    "application/json",
                    """{"message":"slow down"}""",
                    TimeSpan.FromSeconds(5))
            });

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedRule = ruleSummary;
        await viewModel.TestSelectedRuleCommand.ExecuteAsync(null);

        Assert.Equal("请求过于频繁，服务暂时限流。", viewModel.TestStatusMessage);
        Assert.Contains("429", viewModel.LastResponseStatusText);
        Assert.Contains("application/json", viewModel.LastResponseDetailText);
        Assert.Contains("Retry-After", viewModel.LastResponseDetailText);
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

    private sealed class FakeTtsRuleTestService : ITtsRuleTestService
    {
        public TtsRuleTestPreviewResult PreviewResult { get; set; } =
            new(true, "ok", null, [], null);

        public TtsRuleTestResult TestResult { get; set; } =
            new(true, "ok", null, [], null, 200, "audio/wav", null, null);

        public Task<TtsRuleTestPreviewResult> CreatePreviewAsync(TtsRuleTestInput input, CancellationToken cancellationToken)
        {
            return Task.FromResult(PreviewResult);
        }

        public Task<TtsRuleTestResult> TestAsync(TtsRuleTestInput input, CancellationToken cancellationToken)
        {
            return Task.FromResult(TestResult);
        }

        public Task ClearRuleCookiesAsync(long ruleId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
