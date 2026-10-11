using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.PresentationTests.TestDoubles;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class RegexReplacementRulesViewModelTests
{
    [Fact]
    public async Task Cancelled_management_entry_preserves_the_current_rule_draft()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Cancel, ruleCount: 3);
        var vm = fixture.ViewModel;
        await LoadAndSelectFirstAsync(fixture);
        var ids = vm.Rules.Select(rule => rule.Id).ToArray();
        vm.DraftPattern = "Unsaved";
        await vm.SelectRuleWithModifiersAsync(vm.Rules[2], DesktopSelectionModifiers.Control, CancellationToken.None);
        Assert.Equal(ids[0], vm.SelectedRuleId);
        Assert.False(vm.IsManagementMode);
        Assert.Equal("Unsaved", vm.DraftPattern);
        Assert.True(vm.HasUnsavedChanges);
        Assert.Equal(ids[0], vm.SelectedRuleId);
        Assert.Equal(ids[0], Assert.Single(vm.Rules, rule => rule.IsSelected).Id);
    }

    [Fact]
    public async Task Batch_export_finishes_successfully_when_management_mode_is_closed_while_saving()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Discard);
        fixture.Documents.ExportGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.EnterManagementCommand.ExecuteAsync(null);
        vm.SelectAllCommand.Execute(null);
        var export = vm.ExportRuleCommand.ExecuteAsync(null);
        Assert.NotNull(fixture.Documents.ExportedJson);
        Assert.False(export.IsCompleted);
        vm.CancelManagementCommand.Execute(null);
        fixture.Documents.ExportGate.SetResult(true);
        await export;
        Assert.Null(fixture.Feedback.LastProjectedTitle);
        Assert.Equal("正则替换规则已导出", fixture.Feedback.LastSuccessTitle);
    }

    [Fact]
    public async Task Leaving_page_stops_remaining_deletes_after_committed_deletion()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Discard);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        var remainingId = vm.Rules[1].Id;
        await vm.EnterManagementCommand.ExecuteAsync(null);
        vm.SelectAllCommand.Execute(null);
        fixture.Feedback.DeletionDecision = AppConfirmationDecision.Confirm;
        fixture.Workspace.AfterDelete = vm.HandleNavigatedFrom;
        await vm.DeleteSelectedRulesCommand.ExecuteAsync(null);
        Assert.False(vm.IsManagementMode);
        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal(remainingId, Assert.Single(vm.Rules).Id);
    }

    [Fact]
    public async Task Batch_delete_continues_after_failure_without_editor_fallback()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Discard, 3);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        var failedId = vm.Rules[0].Id;
        fixture.Workspace.FailedDeleteId = failedId;
        await vm.SelectRuleCommand.ExecuteAsync(vm.Rules[1]);
        await vm.EnterManagementCommand.ExecuteAsync(null);
        vm.SelectAllCommand.Execute(null);
        fixture.Feedback.DeletionDecision = AppConfirmationDecision.Confirm;
        await vm.DeleteSelectedRulesCommand.ExecuteAsync(null);
        Assert.Equal(failedId, Assert.Single(vm.Rules).Id);
        Assert.False(vm.HasEditor);
        Assert.Equal(1, vm.SelectedCount);
        Assert.Equal(1, fixture.Feedback.DeletionPromptCount);
        Assert.Equal("成功 2，跳过 0，失败 1。", fixture.Feedback.LastWarningMessage);
        Assert.Null(fixture.Feedback.LastSuccessTitle);
    }

    private async Task LoadAsync_leaves_editor_closed_until_a_rule_is_clicked()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Discard);

        await fixture.ViewModel.LoadAsync(CancellationToken.None);

        Assert.False(fixture.ViewModel.HasEditor);
        Assert.Null(fixture.ViewModel.SelectedRuleId);

        await fixture.ViewModel.SelectRuleCommand.ExecuteAsync(fixture.ViewModel.Rules[0]);

        Assert.True(fixture.ViewModel.HasEditor);
        Assert.Equal(fixture.FirstRuleId, fixture.ViewModel.SelectedRuleId);
    }

    private async Task ConfirmLeaveAsync_applies_global_navigation_decision()
    {
        foreach (var (decision, expectedCanLeave) in new[]
                 {
                     (UnsavedChangesDecision.Save, true),
                     (UnsavedChangesDecision.Discard, true),
                     (UnsavedChangesDecision.Cancel, false)
                 })
        {
            var fixture = CreateFixture(decision);
            await LoadAndSelectFirstAsync(fixture);
            fixture.ViewModel.DraftName = "已修改";

            var canLeave = await fixture.ViewModel.ConfirmLeaveAsync(CancellationToken.None);

            Assert.Equal(expectedCanLeave, canLeave);
            Assert.Equal(!expectedCanLeave, fixture.ViewModel.HasUnsavedChanges);
        }
    }

    private async Task SelectRuleAsync_with_unsaved_changes_applies_leave_decision()
    {
        foreach (var (decision, expectedSaveCount) in new[]
                 {
                     (UnsavedChangesDecision.Save, 1),
                     (UnsavedChangesDecision.Discard, 0)
                 })
        {
            var fixture = CreateFixture(decision);
            await LoadAndSelectFirstAsync(fixture);
            fixture.ViewModel.DraftPattern = "已修改";

            await fixture.ViewModel.SelectRuleCommand.ExecuteAsync(fixture.ViewModel.Rules[1]);

            Assert.Equal(fixture.SecondRuleId, fixture.ViewModel.SelectedRuleId);
            Assert.Equal("规则二", fixture.ViewModel.DraftName);
            Assert.Equal(expectedSaveCount, fixture.Workspace.SaveEditorCallCount);
        }
    }

    private async Task SelectRuleAsync_cancel_keeps_current_draft_and_selection()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Cancel);
        await LoadAndSelectFirstAsync(fixture);
        fixture.ViewModel.DraftPattern = "已修改";

        await fixture.ViewModel.SelectRuleCommand.ExecuteAsync(fixture.ViewModel.Rules[1]);

        Assert.Equal(fixture.FirstRuleId, fixture.ViewModel.SelectedRuleId);
        Assert.Equal("已修改", fixture.ViewModel.DraftPattern);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.Equal(0, fixture.Workspace.SaveEditorCallCount);
    }

    private async Task NewRuleAsync_tracks_dirty_and_cancel_closes_editor()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Discard);
        await LoadAndSelectFirstAsync(fixture);

        await fixture.ViewModel.NewRuleCommand.ExecuteAsync(null);

        Assert.True(fixture.ViewModel.IsEditingNewRule);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        fixture.ViewModel.DraftPattern = "新表达式";
        Assert.True(fixture.ViewModel.HasUnsavedChanges);

        await fixture.ViewModel.CancelCommand.ExecuteAsync(null);

        Assert.False(fixture.ViewModel.IsEditingNewRule);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.False(fixture.ViewModel.HasEditor);
        Assert.Null(fixture.ViewModel.SelectedRuleId);
    }

    private async Task Editor_actions_are_disabled_until_the_draft_changes()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Discard);
        await LoadAndSelectFirstAsync(fixture);

        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.True(fixture.ViewModel.CanCancel);
        Assert.False(fixture.ViewModel.CanSave);

        fixture.ViewModel.DraftReplacement = "新替换";

        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.True(fixture.ViewModel.CanCancel);
        Assert.True(fixture.ViewModel.CanSave);

        await fixture.ViewModel.CancelCommand.ExecuteAsync(null);

        Assert.False(fixture.ViewModel.HasEditor);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.False(fixture.ViewModel.CanCancel);
        Assert.False(fixture.ViewModel.CanSave);
    }

    private async Task Validation_projects_name_and_pattern_errors_to_their_form_fields()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        await LoadAndSelectFirstAsync(fixture);

        fixture.ViewModel.DraftName = string.Empty;

        Assert.Equal("规则名称不能为空。", fixture.ViewModel.NameValidationMessage);
        Assert.Empty(fixture.ViewModel.PatternValidationMessage);

        fixture.ViewModel.DraftName = "有效名称";
        fixture.ViewModel.DraftPattern = "[";

        Assert.Empty(fixture.ViewModel.NameValidationMessage);
        Assert.NotEmpty(fixture.ViewModel.PatternValidationMessage);
        Assert.False(fixture.ViewModel.CanSave);
    }

    private async Task Empty_replacement_is_valid_for_removing_matching_text()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        await LoadAndSelectFirstAsync(fixture);

        fixture.ViewModel.DraftReplacement = string.Empty;

        Assert.Empty(fixture.ViewModel.NameValidationMessage);
        Assert.Empty(fixture.ViewModel.PatternValidationMessage);
        Assert.True(fixture.ViewModel.CanSave);
    }

    private async Task SelectRuleAsync_save_failure_keeps_current_draft_and_blocks_leave()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        fixture.Workspace.SaveException = new InvalidOperationException("save failed");
        await LoadAndSelectFirstAsync(fixture);
        fixture.ViewModel.DraftPattern = "已修改";

        await fixture.ViewModel.SelectRuleCommand.ExecuteAsync(fixture.ViewModel.Rules[1]);

        Assert.Equal(fixture.FirstRuleId, fixture.ViewModel.SelectedRuleId);
        Assert.Equal("已修改", fixture.ViewModel.DraftPattern);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.Equal("保存正则替换规则失败", fixture.Feedback.LastProjectedTitle);
    }

    private async Task ConfirmLeaveAsync_does_not_convert_save_cancellation_to_failure()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        fixture.Workspace.SaveException = new OperationCanceledException();
        await LoadAndSelectFirstAsync(fixture);
        fixture.ViewModel.DraftPattern = "已修改";

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.ViewModel.ConfirmLeaveAsync(CancellationToken.None));

        Assert.Equal(fixture.FirstRuleId, fixture.ViewModel.SelectedRuleId);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.Null(fixture.Feedback.LastProjectedTitle);
    }

    [Fact]
    public async Task Regex_rule_editing_contracts_cover_load_selection_and_dirty_leave_decisions()
    {
        await LoadAsync_leaves_editor_closed_until_a_rule_is_clicked();
        await ConfirmLeaveAsync_applies_global_navigation_decision();
        await SelectRuleAsync_with_unsaved_changes_applies_leave_decision();
        await SelectRuleAsync_cancel_keeps_current_draft_and_selection();
        await NewRuleAsync_tracks_dirty_and_cancel_closes_editor();
        await Editor_actions_are_disabled_until_the_draft_changes();
    }

    [Fact]
    public async Task Regex_rule_validation_and_save_contracts_cover_fields_failures_and_cancellation()
    {
        await Validation_projects_name_and_pattern_errors_to_their_form_fields();
        await Empty_replacement_is_valid_for_removing_matching_text();
        await SelectRuleAsync_save_failure_keeps_current_draft_and_blocks_leave();
        await ConfirmLeaveAsync_does_not_convert_save_cancellation_to_failure();
    }

    private static TestFixture CreateFixture(UnsavedChangesDecision decision, int ruleCount = 2)
    {
        var firstRuleId = Guid.NewGuid();
        var secondRuleId = Guid.NewGuid();
        var editors = new List<RegexReplacementRuleEditorModel>
        {
            new(firstRuleId, "规则一", "一", "甲", RegexReplacementScope.Both),
            new(secondRuleId, "规则二", "二", "乙", RegexReplacementScope.Display)
        };
        if (ruleCount >= 3)
        {
            editors.Add(new RegexReplacementRuleEditorModel(
                Guid.NewGuid(),
                "规则三",
                "三",
                "丙",
                RegexReplacementScope.Speech));
        }

        var workspace = new FakeRegexReplacementRuleWorkspaceService(editors.ToArray());
        var feedback = new FakeFeedbackService();
        var documents = new FakeRuleDocumentInteraction();
        var viewModel = new RegexReplacementRulesViewModel(
            workspace,
            feedback,
            new FakeDialogService(decision),
            new FakeNavigationService(),
            documents);
        return new TestFixture(viewModel, workspace, feedback, documents, firstRuleId, secondRuleId);
    }

    private static async Task LoadAndSelectFirstAsync(TestFixture fixture)
    {
        await fixture.ViewModel.LoadAsync(CancellationToken.None);
        await fixture.ViewModel.SelectRuleCommand.ExecuteAsync(fixture.ViewModel.Rules[0]);
    }

    private sealed record TestFixture(
        RegexReplacementRulesViewModel ViewModel,
        FakeRegexReplacementRuleWorkspaceService Workspace,
        FakeFeedbackService Feedback,
        FakeRuleDocumentInteraction Documents,
        Guid FirstRuleId,
        Guid SecondRuleId);

    private sealed class FakeRegexReplacementRuleWorkspaceService : IRegexReplacementRuleWorkspaceService
    {
        private readonly Dictionary<Guid, RegexReplacementRuleEditorModel> _editors;
        private readonly Dictionary<Guid, bool> _enabled;
        private List<Guid> _orderedRuleIds;

        public event EventHandler<RegexReplacementRulesChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public FakeRegexReplacementRuleWorkspaceService(params RegexReplacementRuleEditorModel[] editors)
        {
            _editors = editors.ToDictionary(editor => editor.Id!.Value);
            _enabled = editors.ToDictionary(editor => editor.Id!.Value, _ => true);
            _orderedRuleIds = editors.Select(editor => editor.Id!.Value).ToList();
        }

        public int SaveEditorCallCount { get; private set; }

        public Guid? FailedDeleteId { get; set; }
        public Action? AfterDelete { get; set; }
        public Exception? SaveException { get; set; }
        public Exception? SetEnabledException { get; set; }

        public TaskCompletionSource? SetEnabledGate { get; set; }

        public TaskCompletionSource SetEnabledEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SetEnabledCallCount { get; private set; }

        public TaskCompletionSource? ImportGate { get; set; }

        public TaskCompletionSource ImportEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ImportCallCount { get; private set; }
        public string? LastImportedJson { get; private set; }
        public string ExportedJson { get; set; } = """{"name":"规则"}""";
        public IReadOnlyList<Guid> OrderedRuleIds => _orderedRuleIds;

        public Task<IReadOnlyList<RegexReplacementRuleListItem>> GetRulesAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<RegexReplacementRuleListItem> rules = _orderedRuleIds
                .Select((id, index) => (Editor: _editors[id], IsEnabled: _enabled[id], Index: index))
                .Select(item => new RegexReplacementRuleListItem(
                    item.Editor.Id!.Value,
                    item.Editor.Name,
                    item.Editor.Pattern,
                    item.IsEnabled,
                    (item.Index + 1) * 10,
                    item.Editor.Scope))
                .ToArray();
            return Task.FromResult(rules);
        }

        public Task<string?> ExportRuleJsonAsync(Guid ruleId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(ExportedJson);

        public IReadOnlyList<Guid>? LastExportedIds { get; private set; }
        public Task<string?> ExportRulesJsonAsync(IReadOnlyList<Guid> ruleIds, CancellationToken cancellationToken)
        {
            LastExportedIds = ruleIds.ToArray();
            return ExportRuleJsonAsync(ruleIds[0], cancellationToken);
        }

        public async Task<RuleJsonImportResult> ImportJsonAsync(string json, CancellationToken cancellationToken)
        {
            ImportCallCount++;
            ImportEntered.TrySetResult();
            if (ImportGate is not null)
            {
                await ImportGate.Task.WaitAsync(cancellationToken);
            }

            LastImportedJson = json;
            return new RuleJsonImportResult(1, 0, 1);
        }

        public Task<RegexReplacementRuleEditorModel?> GetEditorAsync(Guid ruleId, CancellationToken cancellationToken)
        {
            return Task.FromResult<RegexReplacementRuleEditorModel?>(_editors.GetValueOrDefault(ruleId));
        }

        public Task<RegexReplacementRuleEditorModel> SaveEditorAsync(
            RegexReplacementRuleEditorModel editor,
            CancellationToken cancellationToken)
        {
            SaveEditorCallCount++;
            if (SaveException is not null)
            {
                throw SaveException;
            }

            var saved = editor.Id is null ? editor with { Id = Guid.NewGuid() } : editor;
            _editors[saved.Id!.Value] = saved;
            if (!_orderedRuleIds.Contains(saved.Id.Value))
            {
                _orderedRuleIds.Add(saved.Id.Value);
                _enabled.Add(saved.Id.Value, true);
            }
            return Task.FromResult(saved);
        }

        public async Task SetRuleEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken)
        {
            SetEnabledCallCount++;
            SetEnabledEntered.TrySetResult();
            if (SetEnabledGate is not null)
            {
                await SetEnabledGate.Task.WaitAsync(cancellationToken);
            }

            if (SetEnabledException is not null)
            {
                throw SetEnabledException;
            }

            _enabled[ruleId] = isEnabled;
        }

        public Task SaveOrderAsync(IReadOnlyList<Guid> orderedRuleIds, CancellationToken cancellationToken)
        {
            _orderedRuleIds = orderedRuleIds.ToList();
            return Task.CompletedTask;
        }

        public Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken)
        {
            if (ruleId == FailedDeleteId) throw new InvalidOperationException("fixture delete failure");
            _editors.Remove(ruleId);
            _enabled.Remove(ruleId);
            _orderedRuleIds.Remove(ruleId);
            AfterDelete?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDialogService : IAppDialogService
    {
        private readonly UnsavedChangesDecision _decision;

        public FakeDialogService(UnsavedChangesDecision decision)
        {
            _decision = decision;
        }

        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken) => Task.FromResult(AppConfirmationDecision.Cancel);

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(
            string title,
            string message,
            string saveButtonText,
            string discardButtonText,
            string cancelButtonText,
            CancellationToken cancellationToken) => Task.FromResult(_decision);
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public string? LastProjectedTitle { get; private set; }
        public string? LastSuccessTitle { get; private set; }
        public string? LastWarningMessage { get; private set; }
        public int DeletionPromptCount { get; private set; }
        public AppConfirmationDecision DeletionDecision { get; set; } = AppConfirmationDecision.Cancel;

        public ProjectedUiError Project(Exception exception) => new("操作失败。", UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            LastProjectedTitle = title;
        }

        public void ShowSuccess(string title, string message)
        {
            LastSuccessTitle = title;
        }

        public void ShowWarning(string title, string message)
        {
            LastWarningMessage = message;
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(
            string title,
            string message,
            CancellationToken cancellationToken)
        {
            DeletionPromptCount++;
            return Task.FromResult(DeletionDecision);
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
