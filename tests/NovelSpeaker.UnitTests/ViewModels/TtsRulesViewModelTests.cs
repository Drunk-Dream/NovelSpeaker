using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class TtsRulesViewModelTests
{
    [Fact]
    public async Task LoadAsync_opens_current_rule_and_marks_selected_list_item()
    {
        var libraryService = new FakeTtsRuleLibraryService(
            [
                new TtsRuleSummary(1, "规则一", true, false, null),
                new TtsRuleSummary(2, "规则二", true, true, null)
            ],
            new TtsRuleEditorModel(
                2,
                "规则二",
                true,
                "https://example.com/tts",
                null,
                null,
                null,
                [],
                new TtsRuleRequestOptionsEditor("GET", null)));
        var viewModel = CreateViewModel(libraryService: libraryService);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasEditor);
        Assert.Equal(2, viewModel.HighlightedRuleId);
        Assert.Equal("规则二", viewModel.DraftName);
        Assert.True(viewModel.Rules.Single(rule => rule.Id == 2).IsSelected);
    }

    [Fact]
    public async Task NewRuleAsync_does_not_add_item_until_saved()
    {
        var libraryService = new FakeTtsRuleLibraryService(
            [new TtsRuleSummary(1, "现有规则", true, true, null)],
            new TtsRuleEditorModel(
                1,
                "现有规则",
                true,
                "https://example.com/old",
                null,
                null,
                null,
                [],
                new TtsRuleRequestOptionsEditor("GET", null)));
        var viewModel = CreateViewModel(libraryService: libraryService);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.NewRuleCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEditingNewRule);
        Assert.Single(viewModel.Rules);

        viewModel.DraftName = "新规则";
        viewModel.DraftUrl = "https://example.com/new";
        await viewModel.SaveDraftCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEditingNewRule);
        Assert.Equal(2, viewModel.Rules.Count);
        Assert.Equal("新规则", viewModel.DraftName);
        Assert.Equal(2, viewModel.HighlightedRuleId);
    }

    [Fact]
    public async Task ImportJsonTextAsync_refreshes_rules_and_selects_first_imported_rule()
    {
        var libraryService = new FakeTtsRuleLibraryService(
            [new TtsRuleSummary(1, "旧规则", true, false, null)],
            new TtsRuleEditorModel(
                1,
                "旧规则",
                true,
                "https://example.com/old",
                null,
                null,
                null,
                [],
                new TtsRuleRequestOptionsEditor("GET", null)))
        {
            ImportResult = new TtsRuleImportResult(1, 1, 3)
            {
                FailedCount = 1,
                FirstImportedRuleId = 2
            },
            RulesAfterImport =
            [
                new TtsRuleSummary(1, "旧规则", true, false, null),
                new TtsRuleSummary(2, "新导入规则", true, true, null)
            ],
            EditorsById =
            {
                [2] = new TtsRuleEditorModel(
                    2,
                    "新导入规则",
                    true,
                    "https://example.com/imported",
                    null,
                    null,
                    null,
                    [],
                    new TtsRuleRequestOptionsEditor("GET", null))
            }
        };
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(libraryService: libraryService, feedbackService: feedback);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.ImportJsonTextAsync("""{"name":"新导入规则","url":"https://example.com/imported"}""", "剪贴板", CancellationToken.None);

        Assert.Equal(2, viewModel.Rules.Count);
        Assert.Equal(2, viewModel.HighlightedRuleId);
        Assert.Equal("新导入规则", viewModel.DraftName);
        Assert.Equal("规则导入完成", feedback.LastTitle);
        Assert.Contains("新增 1 条", feedback.LastMessage);
    }

    [Fact]
    public async Task SelectRuleAsync_with_unsaved_changes_respects_cancel_and_discard()
    {
        var libraryService = new FakeTtsRuleLibraryService(
            [
                new TtsRuleSummary(1, "规则一", true, true, null),
                new TtsRuleSummary(2, "规则二", true, false, null)
            ],
            new TtsRuleEditorModel(
                1,
                "规则一",
                true,
                "https://example.com/one",
                null,
                null,
                null,
                [],
                new TtsRuleRequestOptionsEditor("GET", null)))
        {
            EditorsById =
            {
                [2] = new TtsRuleEditorModel(
                    2,
                    "规则二",
                    true,
                    "https://example.com/two",
                    null,
                    null,
                    null,
                    [],
                    new TtsRuleRequestOptionsEditor("GET", null))
            }
        };
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Cancel
        };
        var viewModel = CreateViewModel(libraryService: libraryService, dialogService: dialogService);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DraftName = "已修改";

        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules.Single(rule => rule.Id == 2));

        Assert.Equal(1, viewModel.HighlightedRuleId);
        Assert.Equal("已修改", viewModel.DraftName);

        dialogService.NextUnsavedDecision = UnsavedChangesDecision.Discard;
        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules.Single(rule => rule.Id == 2));

        Assert.Equal(2, viewModel.HighlightedRuleId);
        Assert.Equal("规则二", viewModel.DraftName);
    }

    [Fact]
    public async Task TestDraftAsync_uses_unsaved_draft_values()
    {
        var libraryService = new FakeTtsRuleLibraryService(
            [new TtsRuleSummary(7, "测试规则", true, true, null)],
            new TtsRuleEditorModel(
                7,
                "测试规则",
                true,
                "https://example.com/tts",
                null,
                null,
                null,
                [],
                new TtsRuleRequestOptionsEditor("GET", null)));
        var ruleTestService = new FakeTtsRuleTestService();
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(libraryService: libraryService, ruleTestService: ruleTestService, feedbackService: feedback);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DraftUrl = "https://example.com/changed";
        viewModel.AddHeaderEntryCommand.Execute(null);
        viewModel.HeaderEntries[0].Key = "Authorization";
        viewModel.HeaderEntries[0].Value = "Bearer demo";

        await viewModel.TestDraftCommand.ExecuteAsync(null);

        Assert.NotNull(ruleTestService.LastInput);
        Assert.Equal("https://example.com/changed", ruleTestService.LastInput!.Editor.Url);
        Assert.Equal("Authorization", ruleTestService.LastInput.Editor.Headers[0].Key);
        Assert.Equal("Bearer demo", ruleTestService.LastInput.Editor.Headers[0].Value);
        Assert.Equal("试听已开始", feedback.LastTitle);
    }

    [Fact]
    public async Task DeleteRuleAsync_current_rule_clears_selected_rule_after_confirmation()
    {
        var libraryService = new FakeTtsRuleLibraryService(
            [new TtsRuleSummary(5, "当前规则", true, true, null)],
            new TtsRuleEditorModel(
                5,
                "当前规则",
                true,
                "https://example.com/current",
                null,
                null,
                null,
                [],
                new TtsRuleRequestOptionsEditor("GET", null)));
        var dialogService = new FakeAppDialogService
        {
            NextConfirmationDecision = AppConfirmationDecision.Confirm
        };
        var viewModel = CreateViewModel(libraryService: libraryService, dialogService: dialogService);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.DeleteRuleCommand.ExecuteAsync(null);

        Assert.NotNull(libraryService.LastMutationDecision);
        Assert.True(libraryService.LastMutationDecision!.ClearSelectedRule);
        Assert.Equal(TtsRuleMutationAction.Delete, libraryService.LastMutationDecision.Action);
    }

    private static TtsRulesViewModel CreateViewModel(
        FakeTtsRuleLibraryService? libraryService = null,
        FakeTtsRuleTestService? ruleTestService = null,
        FakeFeedbackService? feedbackService = null,
        FakeAppDialogService? dialogService = null)
    {
        return new TtsRulesViewModel(
            libraryService ?? new FakeTtsRuleLibraryService([], null),
            ruleTestService ?? new FakeTtsRuleTestService(),
            feedbackService ?? new FakeFeedbackService(),
            dialogService ?? new FakeAppDialogService(),
            new FakeAppSettingsService(),
            new FakeNavigationService());
    }

    private sealed class FakeTtsRuleLibraryService : ITtsRuleLibraryService
    {
        private IReadOnlyList<TtsRuleSummary> _rules;

        public FakeTtsRuleLibraryService(IReadOnlyList<TtsRuleSummary> rules, TtsRuleEditorModel? defaultEditor)
        {
            _rules = rules;
            DefaultEditor = defaultEditor;
        }

        public TtsRuleImportResult ImportResult { get; set; } = new(0, 0, 0);

        public IReadOnlyList<TtsRuleSummary>? RulesAfterImport { get; set; }

        public TtsRuleEditorModel? DefaultEditor { get; }

        public Dictionary<long, TtsRuleEditorModel> EditorsById { get; } = [];

        public TtsRuleMutationDecision? LastMutationDecision { get; private set; }

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_rules);
        }

        public Task<TtsRuleImportPreview> CreateImportPreviewAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TtsRuleImportResult> ImportJsonTextAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken)
        {
            if (RulesAfterImport is not null)
            {
                _rules = RulesAfterImport;
            }

            return Task.FromResult(ImportResult);
        }

        public Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string> ExportEditorJsonAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
        {
            return Task.FromResult($$"""{"name":"{{editor.Name}}","url":"{{editor.Url}}"}""");
        }

        public Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken)
        {
            if (EditorsById.TryGetValue(ruleId, out var editor))
            {
                return Task.FromResult<TtsRuleEditorModel?>(editor);
            }

            return Task.FromResult<TtsRuleEditorModel?>(DefaultEditor);
        }

        public Task<TtsRuleValidationResult> ValidateEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
        {
            return Task.FromResult(new TtsRuleValidationResult(true, [], editor));
        }

        public Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
        {
            var ruleId = editor.Id ?? (_rules.Count == 0 ? 1 : _rules.Max(rule => rule.Id) + 1);
            var savedRule = new HttpTtsRule(
                ruleId,
                editor.Name,
                editor.Url,
                editor.ContentType,
                editor.ConcurrentRate,
                null,
                null,
                editor.LastUpdateTime,
                editor.IsEnabled,
                null,
                "created",
                "updated");

            var savedEditor = editor with { Id = ruleId };
            EditorsById[ruleId] = savedEditor;

            var summaries = _rules.Where(rule => rule.Id != ruleId).ToList();
            summaries.Add(new TtsRuleSummary(ruleId, editor.Name, editor.IsEnabled, false, null));
            _rules = summaries.OrderBy(rule => rule.Id).ToArray();
            return Task.FromResult(savedRule);
        }

        public Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(long ruleId, TtsRuleMutationAction action, CancellationToken cancellationToken)
        {
            return Task.FromResult(new TtsRuleProtectionInfo(ruleId, action, true, false, true, []));
        }

        public Task<TtsRuleMutationResult> ApplyRuleMutationAsync(TtsRuleMutationDecision decision, CancellationToken cancellationToken)
        {
            LastMutationDecision = decision;
            _rules = _rules.Where(rule => rule.Id != decision.RuleId).ToArray();
            return Task.FromResult(new TtsRuleMutationResult(decision.RuleId, decision.Action, null, true));
        }

        public Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken)
        {
            _rules = _rules
                .Select(rule => rule with { IsSelected = ruleId == rule.Id })
                .ToArray();
            return Task.CompletedTask;
        }

        public Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken)
        {
            _rules = _rules.Where(rule => rule.Id != ruleId).ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTtsRuleTestService : ITtsRuleTestService
    {
        public TtsRuleDraftTestInput? LastInput { get; private set; }

        public Task<TtsRuleTestResult> TestAsync(TtsRuleDraftTestInput input, CancellationToken cancellationToken)
        {
            LastInput = input;
            return Task.FromResult(new TtsRuleTestResult(true, "试听成功。", null, [], null, 200, "audio/wav", null, null));
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public string? LastTitle { get; private set; }

        public string? LastMessage { get; private set; }

        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            LastTitle = title;
            LastMessage = projected.UserMessage;
        }

        public void ShowSuccess(string title, string message)
        {
            LastTitle = title;
            LastMessage = message;
        }

        public void ShowWarning(string title, string message)
        {
            LastTitle = title;
            LastMessage = message;
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            return Task.FromResult(AppConfirmationDecision.Confirm);
        }
    }

    private sealed class FakeAppDialogService : IAppDialogService
    {
        public UnsavedChangesDecision NextUnsavedDecision { get; set; } = UnsavedChangesDecision.Discard;

        public AppConfirmationDecision NextConfirmationDecision { get; set; } = AppConfirmationDecision.Cancel;

        public Task<AppConfirmationDecision> ShowConfirmationAsync(string title, string message, string primaryButtonText, string closeButtonText, CancellationToken cancellationToken)
        {
            return Task.FromResult(NextConfirmationDecision);
        }

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(string title, string message, string saveButtonText, string discardButtonText, string cancelButtonText, CancellationToken cancellationToken)
        {
            return Task.FromResult(NextUnsavedDecision);
        }
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(AppSettings.Default with { DefaultSpeakSpeed = 12 });
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            return Task.FromResult(AppSettings.Default);
        }
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public INavigationView GetNavigationControl() => throw new NotSupportedException();
        public bool GoBack() => true;
        public bool Navigate(Type pageType) => true;
        public bool Navigate(Type pageType, object? dataContext) => true;
        public bool Navigate(string pageIdOrTargetTag) => true;
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;
        public bool NavigateWithHierarchy(Type pageType) => true;
        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => true;
        public void SetNavigationControl(INavigationView navigation) { }
    }
}
