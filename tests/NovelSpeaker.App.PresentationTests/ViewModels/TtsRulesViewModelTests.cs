using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.PresentationTests.TestDoubles;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class TtsRulesViewModelTests
{
    private async Task NewRuleAsync_does_not_add_item_until_saved()
    {
        var useCases = new TtsRuleUseCaseStub(
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
        var viewModel = CreateViewModel(useCases: useCases);
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

    private async Task SaveDraftAsync_shows_validation_warning_without_saving()
    {
        var editor = new TtsRuleEditorModel(
            1,
            "规则一",
            true,
            "https://example.com/tts",
            null,
            null,
            null,
            [],
            new TtsRuleRequestOptionsEditor("GET", null));
        var useCases = new TtsRuleUseCaseStub(
            [new TtsRuleSummary(1, "规则一", true, true, null)],
            editor)
        {
            ValidationResult = new TtsRuleValidationResult(
                false,
                ["请求 URL 无效。"],
                editor)
        };
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(useCases: useCases, feedbackService: feedback);
        await LoadAndSelectAsync(viewModel, 1);
        await viewModel.SaveDraftCommand.ExecuteAsync(null);

        Assert.Equal("无法保存规则", feedback.LastTitle);
        Assert.Contains("请求 URL 无效", feedback.LastMessage);
        Assert.Equal(0, useCases.SaveCallCount);
    }

    private async Task SelectRuleAsync_with_unsaved_changes_saves_before_leaving_when_requested()
    {
        var firstEditor = new TtsRuleEditorModel(
            1,
            "规则一",
            true,
            "https://example.com/one",
            null,
            null,
            null,
            [],
            new TtsRuleRequestOptionsEditor("GET", null));
        var useCases = new TtsRuleUseCaseStub(
            [
                new TtsRuleSummary(1, "规则一", true, true, null),
                new TtsRuleSummary(2, "规则二", true, false, null)
            ],
            firstEditor)
        {
            EditorsById =
            {
                [1] = firstEditor,
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
        var viewModel = CreateViewModel(
            useCases: useCases,
            dialogService: new FakeAppDialogService { NextUnsavedDecision = UnsavedChangesDecision.Save });
        await LoadAndSelectAsync(viewModel, 1);
        viewModel.DraftName = "已保存的规则一";

        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules.Single(rule => rule.Id == 2));

        Assert.Equal("已保存的规则一", useCases.EditorsById[1].Name);
        Assert.Equal(2, viewModel.HighlightedRuleId);
        Assert.Equal("规则二", viewModel.DraftName);
    }

    [Fact]
    public async Task Tts_rule_creation_contract_covers_save_refresh()
    {
        await NewRuleAsync_does_not_add_item_until_saved();
    }

    [Fact]
    public async Task Tts_rule_editing_contracts_cover_validation_and_unsaved_selection()
    {
        await SaveDraftAsync_shows_validation_warning_without_saving();
        await SelectRuleAsync_with_unsaved_changes_saves_before_leaving_when_requested();
    }

    private static TtsRuleEditorModel CreateEditor(long id, string name, bool isEnabled)
    {
        return new TtsRuleEditorModel(
            id,
            name,
            isEnabled,
            $"https://example.com/{id}",
            null,
            null,
            null,
            [],
            new TtsRuleRequestOptionsEditor("GET", null));
    }

    private static async Task LoadAndSelectAsync(TtsRulesViewModel viewModel, long ruleId)
    {
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules.Single(rule => rule.Id == ruleId));
    }

    private static TtsRulesViewModel CreateViewModel(
        TtsRuleUseCaseStub? useCases = null,
        FakeTtsRuleTestService? ruleTestService = null,
        FakeFeedbackService? feedbackService = null,
        FakeAppDialogService? dialogService = null,
        IRuleDocumentInteraction? ruleDocuments = null)
    {
        return new TtsRulesViewModel(
            useCases ??= new TtsRuleUseCaseStub([], null),
            useCases,
            useCases,
            ruleTestService ?? new FakeTtsRuleTestService(),
            feedbackService ?? new FakeFeedbackService(),
            dialogService ?? new FakeAppDialogService(),
            new FakeAppSettingsService(),
            new FakeNavigationService(),
            ruleDocuments ?? new FakeRuleDocumentInteraction());
    }

    private sealed class TtsRuleUseCaseStub :
        ITtsRuleEditorUseCase,
        ITtsRuleSelectionUseCase,
        ITtsRuleQueries
    {
        private IReadOnlyList<TtsRuleSummary> _rules;

        public event EventHandler<TtsRuleChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public TtsRuleUseCaseStub(IReadOnlyList<TtsRuleSummary> rules, TtsRuleEditorModel? defaultEditor)
        {
            _rules = rules;
            DefaultEditor = defaultEditor;
        }

        public TtsRuleValidationResult? ValidationResult { get; set; }

        public int SaveCallCount { get; private set; }

        public TaskCompletionSource? SetEnabledGate { get; init; }

        public TaskCompletionSource SetEnabledEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TtsRuleEditorModel? DefaultEditor { get; }

        public Dictionary<long, TtsRuleEditorModel> EditorsById { get; } = [];

        public TtsRuleMutationDecision? LastMutationDecision { get; private set; }

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_rules);
        }

        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>("""{"name":"规则"}""");
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
            return Task.FromResult(ValidationResult ?? new TtsRuleValidationResult(true, [], editor));
        }

        public Task<TtsRuleDraftPreparationResult> PrepareDraftAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
        {
            SaveCallCount++;
            var ruleId = editor.Id ?? (_rules.Count == 0 ? 1 : _rules.Max(rule => rule.Id) + 1);
            var savedRule = TestHttpTtsRules.Create(
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

        public async Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken)
        {
            SetEnabledEntered.SetResult();
            if (SetEnabledGate is not null)
            {
                await SetEnabledGate.Task.WaitAsync(cancellationToken);
            }

            var editor = EditorsById.TryGetValue(ruleId, out var storedEditor)
                ? storedEditor
                : DefaultEditor ?? throw new InvalidOperationException("规则不存在。");
            EditorsById[ruleId] = editor with { Id = ruleId, IsEnabled = isEnabled };
            _rules = _rules
                .Select(rule => rule.Id == ruleId ? rule with { IsEnabled = isEnabled } : rule)
                .ToArray();
        }

        public Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(long ruleId, TtsRuleMutationAction action, CancellationToken cancellationToken)
        {
            return Task.FromResult(new TtsRuleProtectionInfo(ruleId, action, true, false, true, []));
        }

        public Task<TtsRuleMutationResult> ApplyRuleMutationAsync(TtsRuleMutationDecision decision, CancellationToken cancellationToken)
        {
            LastMutationDecision = decision;
            _rules = decision.Action switch
            {
                TtsRuleMutationAction.Disable => _rules
                    .Select(rule => rule.Id == decision.RuleId
                        ? rule with { IsEnabled = false, IsSelected = false }
                        : rule)
                    .ToArray(),
                TtsRuleMutationAction.Delete => _rules.Where(rule => rule.Id != decision.RuleId).ToArray(),
                _ => throw new ArgumentOutOfRangeException(nameof(decision))
            };
            return Task.FromResult(new TtsRuleMutationResult(decision.RuleId, decision.Action, null, true));
        }

        public Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken)
        {
            _rules = _rules
                .Select(rule => rule with { IsSelected = ruleId == rule.Id })
                .ToArray();
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

        public int UnsavedChangesPromptCount { get; private set; }

        public Task<AppConfirmationDecision> ShowConfirmationAsync(string title, string message, string primaryButtonText, string closeButtonText, CancellationToken cancellationToken)
        {
            return Task.FromResult(NextConfirmationDecision);
        }

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(string title, string message, string saveButtonText, string discardButtonText, string cancelButtonText, CancellationToken cancellationToken)
        {
            UnsavedChangesPromptCount++;
            return Task.FromResult(NextUnsavedDecision);
        }
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public AppSettings Current => AppSettings.Default with { DefaultSpeakSpeed = 12 };
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            return Task.FromResult(AppSettings.Default);
        }
    }

    private sealed class FakeNavigationService : IAppNavigator
    {
        public AppRoute CurrentRoute => AppRoutes.Library;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(false);

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(true);
    }
}
