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

    private async Task ImportJsonTextAsync_refreshes_rules_without_opening_imported_rule()
    {
        var useCases = new TtsRuleUseCaseStub(
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
        var documents = new FakeRuleDocumentInteraction
        {
            ClipboardDocument = new RuleImportDocument(
                """{"name":"新导入规则","url":"https://example.com/imported"}""",
                "剪贴板")
        };
        var viewModel = CreateViewModel(
            useCases: useCases,
            feedbackService: feedback,
            ruleDocuments: documents);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.ImportRulesFromClipboardAsync(CancellationToken.None);

        Assert.Equal(2, viewModel.Rules.Count);
        Assert.Null(viewModel.HighlightedRuleId);
        Assert.False(viewModel.HasEditor);
        Assert.Equal("部分规则导入失败", feedback.LastTitle);
        Assert.Contains("新增 1 条", feedback.LastMessage);
    }

    private async Task ImportJsonTextAsync_shows_safe_cookie_login_info_warning_with_mixed_counts()
    {
        var incompatibleRule = CreateImportItem(
            0,
            false,
            "当前版本不支持 Cookie/LoginInfo；该规则不能导入。");
        var compatibleRule = CreateImportItem(1, true, "可以导入。");
        var useCases = new TtsRuleUseCaseStub([], null)
        {
            ImportPreview = new TtsRuleImportPreview("剪贴板", [incompatibleRule, compatibleRule], null),
            ImportResult = new TtsRuleImportResult(1, 0, 2) { FailedCount = 1 }
        };
        var feedback = new FakeFeedbackService();
        var documents = new FakeRuleDocumentInteraction
        {
            ClipboardDocument = new RuleImportDocument("[]", "剪贴板")
        };
        var viewModel = CreateViewModel(
            useCases: useCases,
            feedbackService: feedback,
            ruleDocuments: documents);

        await viewModel.ImportRulesFromClipboardAsync(CancellationToken.None);

        Assert.Equal("部分规则不兼容", feedback.LastTitle);
        Assert.Contains("当前版本不支持 Cookie/LoginInfo", feedback.LastMessage);
        Assert.Contains("新增 1 条，失败 1 条，跳过 0 条", feedback.LastMessage);
        Assert.DoesNotContain("secret", feedback.LastMessage);
    }

    private async Task SaveDraftAsync_shows_explicit_validation_warning_for_cookie_header()
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
                ["当前版本不支持 Cookie/LoginInfo；请移除相关依赖。"],
                editor)
        };
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(useCases: useCases, feedbackService: feedback);
        await LoadAndSelectAsync(viewModel, 1);
        viewModel.AddHeaderEntryCommand.Execute(null);
        viewModel.HeaderEntries[0].Key = "Cookie";
        viewModel.HeaderEntries[0].Value = "session=secret";

        await viewModel.SaveDraftCommand.ExecuteAsync(null);

        Assert.Equal("规则不兼容", feedback.LastTitle);
        Assert.Contains("Cookie/LoginInfo", feedback.LastMessage);
        Assert.DoesNotContain("secret", feedback.LastMessage);
        Assert.Equal(0, useCases.SaveCallCount);
    }

    private async Task ExportRuleAsync_uses_the_tts_rule_file_name()
    {
        var useCases = new TtsRuleUseCaseStub(
            [new TtsRuleSummary(3, "规则", true, false, null)],
            CreateEditor(3, "规则", true));
        var documents = new FakeRuleDocumentInteraction();
        var viewModel = CreateViewModel(useCases: useCases, ruleDocuments: documents);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.ExportRuleAsync(viewModel.Rules.Single(), CancellationToken.None);

        Assert.Equal("tts-rule.json", documents.ExportedFileName);
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

    private async Task TestDraftAsync_uses_unsaved_draft_values()
    {
        var useCases = new TtsRuleUseCaseStub(
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
        var viewModel = CreateViewModel(useCases: useCases, ruleTestService: ruleTestService, feedbackService: feedback);
        await LoadAndSelectAsync(viewModel, 7);
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

    private async Task ToggleRuleEnabledAsync_disables_from_card_and_clears_current_without_dirty_prompt()
    {
        var useCases = new TtsRuleUseCaseStub(
            [new TtsRuleSummary(5, "当前规则", true, true, null)],
            CreateEditor(5, "当前规则", true));
        var dialogService = new FakeAppDialogService { NextUnsavedDecision = UnsavedChangesDecision.Cancel };
        var viewModel = CreateViewModel(useCases: useCases, dialogService: dialogService);
        await LoadAndSelectAsync(viewModel, 5);
        viewModel.DraftName = "未保存草稿";

        await viewModel.ToggleRuleEnabledCommand.ExecuteAsync(viewModel.Rules.Single());

        Assert.NotNull(useCases.LastMutationDecision);
        Assert.Equal(TtsRuleMutationAction.Disable, useCases.LastMutationDecision!.Action);
        Assert.True(useCases.LastMutationDecision.ClearSelectedRule);
        Assert.False(viewModel.Rules.Single().IsEnabled);
        Assert.False(viewModel.Rules.Single().IsCurrent);
        Assert.Equal(0, dialogService.UnsavedChangesPromptCount);
        Assert.Equal("未保存草稿", viewModel.DraftName);
    }

    private async Task ToggleRuleEnabledAsync_enables_disabled_card_immediately()
    {
        var editor = CreateEditor(8, "备用规则", false);
        var useCases = new TtsRuleUseCaseStub(
            [new TtsRuleSummary(8, "备用规则", false, false, null)],
            editor);
        var viewModel = CreateViewModel(useCases: useCases);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.ToggleRuleEnabledCommand.ExecuteAsync(viewModel.Rules.Single());

        Assert.True(useCases.EditorsById[8].IsEnabled);
        Assert.True(viewModel.Rules.Single().IsEnabled);
    }

    private async Task ToggleRuleEnabledAsync_serializes_with_draft_save()
    {
        var useCases = new TtsRuleUseCaseStub(
            [new TtsRuleSummary(8, "备用规则", false, false, null)],
            CreateEditor(8, "备用规则", false))
        {
            SetEnabledGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var viewModel = CreateViewModel(useCases: useCases);
        await LoadAndSelectAsync(viewModel, 8);
        viewModel.DraftName = "已修改名称";

        var toggleTask = viewModel.ToggleRuleEnabledCommand.ExecuteAsync(viewModel.Rules.Single());
        await useCases.SetEnabledEntered.Task;

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanSaveDraft);
        await viewModel.SaveDraftCommand.ExecuteAsync(null);
        Assert.Equal(0, useCases.SaveCallCount);

        useCases.SetEnabledGate.SetResult();
        await toggleTask;
        await viewModel.SaveDraftCommand.ExecuteAsync(null);

        Assert.Equal(1, useCases.SaveCallCount);
        Assert.True(useCases.EditorsById[8].IsEnabled);
        Assert.Equal("已修改名称", useCases.EditorsById[8].Name);
    }

    private async Task DeleteRuleAsync_current_rule_clears_selected_rule_after_confirmation()
    {
        var useCases = new TtsRuleUseCaseStub(
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
        var viewModel = CreateViewModel(useCases: useCases, dialogService: dialogService);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.DeleteRuleCommand.ExecuteAsync(viewModel.Rules.Single());

        Assert.NotNull(useCases.LastMutationDecision);
        Assert.True(useCases.LastMutationDecision!.ClearSelectedRule);
        Assert.Equal(TtsRuleMutationAction.Delete, useCases.LastMutationDecision.Action);
    }

    [Fact]
    public async Task Tts_rule_creation_and_import_contracts_cover_save_refresh_and_safe_warnings()
    {
        await NewRuleAsync_does_not_add_item_until_saved();
        await ImportJsonTextAsync_refreshes_rules_without_opening_imported_rule();
        await ImportJsonTextAsync_shows_safe_cookie_login_info_warning_with_mixed_counts();
    }

    [Fact]
    public async Task Tts_rule_editing_contracts_cover_validation_export_and_unsaved_selection()
    {
        await SaveDraftAsync_shows_explicit_validation_warning_for_cookie_header();
        await ExportRuleAsync_uses_the_tts_rule_file_name();
        await SelectRuleAsync_with_unsaved_changes_saves_before_leaving_when_requested();
    }

    [Fact]
    public async Task Tts_rule_testing_and_toggle_contracts_cover_draft_values_busy_state_and_enablement()
    {
        await TestDraftAsync_uses_unsaved_draft_values();
        await ToggleRuleEnabledAsync_disables_from_card_and_clears_current_without_dirty_prompt();
        await ToggleRuleEnabledAsync_enables_disabled_card_immediately();
        await ToggleRuleEnabledAsync_serializes_with_draft_save();
    }

    [Fact]
    public async Task Tts_rule_delete_contracts_clear_current_selection_after_confirmation()
    {
        await DeleteRuleAsync_current_rule_clears_selected_rule_after_confirmation();
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
            useCases,
            ruleTestService ?? new FakeTtsRuleTestService(),
            feedbackService ?? new FakeFeedbackService(),
            dialogService ?? new FakeAppDialogService(),
            new FakeAppSettingsService(),
            new FakeNavigationService(),
            ruleDocuments ?? new FakeRuleDocumentInteraction());
    }

    private static TtsRuleImportItem CreateImportItem(int index, bool canImport, string statusMessage)
    {
        var utcNow = DateTime.UtcNow.ToString("O");
        return new TtsRuleImportItem(
            index,
            $"规则 {index}",
            "https://example.com/tts",
            canImport ? TtsRuleCompatibilityStatus.Compatible : TtsRuleCompatibilityStatus.NeedsManualAdjustment,
            [],
            canImport,
            canImport,
            false,
            false,
            statusMessage,
            TestHttpTtsRules.Create(
                0,
                $"规则 {index}",
                "https://example.com/tts",
                null,
                null,
                null,
                null,
                null,
                true,
                null,
                utcNow,
                utcNow));
    }

    private sealed class TtsRuleUseCaseStub :
        ITtsRuleImportUseCase,
        ITtsRuleEditorUseCase,
        ITtsRuleSelectionUseCase,
        ITtsRuleQueries
    {
        private IReadOnlyList<TtsRuleSummary> _rules;

        public TtsRuleUseCaseStub(IReadOnlyList<TtsRuleSummary> rules, TtsRuleEditorModel? defaultEditor)
        {
            _rules = rules;
            DefaultEditor = defaultEditor;
        }

        public TtsRuleImportResult ImportResult { get; set; } = new(0, 0, 0);

        public TtsRuleImportPreview? ImportPreview { get; set; }

        public TtsRuleValidationResult? ValidationResult { get; set; }

        public int SaveCallCount { get; private set; }

        public TaskCompletionSource? SetEnabledGate { get; init; }

        public TaskCompletionSource SetEnabledEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            return Task.FromResult(ImportPreview ?? new TtsRuleImportPreview(sourceDescription, [], null));
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
            if (RulesAfterImport is not null)
            {
                _rules = RulesAfterImport;
            }

            return Task.FromResult(ImportResult);
        }

        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>("""{"name":"规则"}""");
        }

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
