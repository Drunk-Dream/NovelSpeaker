using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.Domain.Books;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class RegexReplacementRulesViewModelTests
{
    [Fact]
    public async Task LoadAsync_leaves_editor_closed_until_a_rule_is_clicked()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Discard);

        await fixture.ViewModel.LoadAsync(CancellationToken.None);

        Assert.False(fixture.ViewModel.HasEditor);
        Assert.Null(fixture.ViewModel.SelectedRuleId);

        await fixture.ViewModel.SelectRuleCommand.ExecuteAsync(fixture.ViewModel.Rules[0]);

        Assert.True(fixture.ViewModel.HasEditor);
        Assert.Equal(fixture.FirstRuleId, fixture.ViewModel.SelectedRuleId);
    }

    [Theory]
    [InlineData(UnsavedChangesDecision.Save, true)]
    [InlineData(UnsavedChangesDecision.Discard, true)]
    [InlineData(UnsavedChangesDecision.Cancel, false)]
    public async Task ConfirmLeaveAsync_applies_global_navigation_decision(
        UnsavedChangesDecision decision,
        bool expectedCanLeave)
    {
        var fixture = CreateFixture(decision);
        await LoadAndSelectFirstAsync(fixture);
        fixture.ViewModel.DraftName = "已修改";

        var canLeave = await fixture.ViewModel.ConfirmLeaveAsync(CancellationToken.None);

        Assert.Equal(expectedCanLeave, canLeave);
        Assert.Equal(!expectedCanLeave, fixture.ViewModel.HasUnsavedChanges);
    }

    [Theory]
    [InlineData(UnsavedChangesDecision.Save, 1)]
    [InlineData(UnsavedChangesDecision.Discard, 0)]
    public async Task SelectRuleAsync_with_unsaved_changes_applies_leave_decision(
        UnsavedChangesDecision decision,
        int expectedSaveCount)
    {
        var fixture = CreateFixture(decision);
        await LoadAndSelectFirstAsync(fixture);
        fixture.ViewModel.DraftPattern = "已修改";

        await fixture.ViewModel.SelectRuleCommand.ExecuteAsync(fixture.ViewModel.Rules[1]);

        Assert.Equal(fixture.SecondRuleId, fixture.ViewModel.SelectedRuleId);
        Assert.Equal("规则二", fixture.ViewModel.DraftName);
        Assert.Equal(expectedSaveCount, fixture.Workspace.SaveEditorCallCount);
    }

    [Fact]
    public async Task SelectRuleAsync_cancel_keeps_current_draft_and_selection()
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

    [Fact]
    public async Task NewRuleAsync_tracks_dirty_and_cancel_closes_editor()
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

    [Fact]
    public async Task Editor_actions_are_disabled_until_the_draft_changes()
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

    [Fact]
    public async Task SelectRuleAsync_save_failure_keeps_current_draft_and_blocks_leave()
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

    [Fact]
    public async Task ConfirmLeaveAsync_does_not_convert_save_cancellation_to_failure()
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
    public async Task SaveAsync_refreshes_playback_when_execution_fields_change()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        await LoadAndSelectFirstAsync(fixture);
        fixture.ViewModel.DraftReplacement = "新替换";

        await fixture.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Playback.RegexRefreshCount);
    }

    [Fact]
    public async Task SaveAsync_does_not_refresh_playback_when_only_name_changes()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        await LoadAndSelectFirstAsync(fixture);
        fixture.ViewModel.DraftName = "新名称";

        await fixture.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, fixture.Playback.RegexRefreshCount);
    }

    [Fact]
    public async Task ToggleEnabledAsync_persists_state_and_refreshes_current_playback()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        await fixture.ViewModel.LoadAsync(CancellationToken.None);
        var rule = fixture.ViewModel.Rules[0];

        await fixture.ViewModel.ToggleEnabledCommand.ExecuteAsync(rule);

        Assert.False(fixture.ViewModel.Rules[0].IsEnabled);
        Assert.Equal(1, fixture.Playback.RegexRefreshCount);
    }

    [Fact]
    public async Task ToggleEnabledAsync_cancellation_rolls_back_immediately_and_propagates()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        fixture.Workspace.SetEnabledException = new OperationCanceledException();
        await fixture.ViewModel.LoadAsync(CancellationToken.None);
        var rule = fixture.ViewModel.Rules[0];

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.ViewModel.ToggleEnabledCommand.ExecuteAsync(rule));

        Assert.True(rule.IsEnabled);
        Assert.Equal("已启用", rule.EnabledStateText);
        Assert.Equal(0, fixture.Playback.RegexRefreshCount);
        Assert.Null(fixture.Feedback.LastProjectedTitle);
    }

    [Fact]
    public async Task ToggleEnabledAsync_playback_refresh_cancellation_keeps_persisted_list_state_and_propagates()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        fixture.Playback.RefreshException = new OperationCanceledException();
        await fixture.ViewModel.LoadAsync(CancellationToken.None);
        var optimisticItem = fixture.ViewModel.Rules[0];

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.ViewModel.ToggleEnabledCommand.ExecuteAsync(optimisticItem));

        var currentItem = fixture.ViewModel.Rules[0];
        Assert.NotSame(optimisticItem, currentItem);
        Assert.DoesNotContain(optimisticItem, fixture.ViewModel.Rules);
        Assert.False(currentItem.IsEnabled);
        Assert.Equal("已禁用", currentItem.EnabledStateText);
        Assert.Equal(1, fixture.Playback.RegexRefreshCount);
        Assert.Null(fixture.Feedback.LastProjectedTitle);
    }

    [Fact]
    public async Task MoveRuleDownFromListAsync_persists_order_updates_boundaries_and_refreshes_playback()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        await fixture.ViewModel.LoadAsync(CancellationToken.None);

        await fixture.ViewModel.MoveRuleDownFromListAsync(
            fixture.ViewModel.Rules[0],
            CancellationToken.None);

        Assert.Equal([fixture.SecondRuleId, fixture.FirstRuleId], fixture.Workspace.OrderedRuleIds);
        Assert.Equal(fixture.SecondRuleId, fixture.ViewModel.Rules[0].Id);
        Assert.False(fixture.ViewModel.Rules[0].CanMoveUp);
        Assert.True(fixture.ViewModel.Rules[0].CanMoveDown);
        Assert.True(fixture.ViewModel.Rules[1].CanMoveUp);
        Assert.False(fixture.ViewModel.Rules[1].CanMoveDown);
        Assert.Equal(1, fixture.Playback.RegexRefreshCount);
    }

    [Fact]
    public async Task DeleteRuleFromListAsync_deletes_the_menu_target_without_closing_another_editor()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        fixture.Feedback.DeletionDecision = AppConfirmationDecision.Confirm;
        await LoadAndSelectFirstAsync(fixture);

        await fixture.ViewModel.DeleteRuleFromListAsync(
            fixture.ViewModel.Rules[1],
            CancellationToken.None);

        Assert.Equal([fixture.FirstRuleId], fixture.Workspace.OrderedRuleIds);
        Assert.Equal(fixture.FirstRuleId, fixture.ViewModel.SelectedRuleId);
        Assert.Equal("规则一", fixture.ViewModel.DraftName);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.Equal(1, fixture.Playback.RegexRefreshCount);
    }

    private static TestFixture CreateFixture(UnsavedChangesDecision decision)
    {
        var firstRuleId = Guid.NewGuid();
        var secondRuleId = Guid.NewGuid();
        var workspace = new FakeRegexReplacementRuleWorkspaceService(
            new RegexReplacementRuleEditorModel(firstRuleId, "规则一", "一", "甲", RegexReplacementScope.Both),
            new RegexReplacementRuleEditorModel(secondRuleId, "规则二", "二", "乙", RegexReplacementScope.Display));
        var feedback = new FakeFeedbackService();
        var playback = new FakePlaybackCoordinator();
        var viewModel = new RegexReplacementRulesViewModel(
            workspace,
            playback,
            feedback,
            new FakeDialogService(decision),
            new FakeNavigationService());
        return new TestFixture(viewModel, workspace, feedback, playback, firstRuleId, secondRuleId);
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
        FakePlaybackCoordinator Playback,
        Guid FirstRuleId,
        Guid SecondRuleId);

    private sealed class FakeRegexReplacementRuleWorkspaceService : IRegexReplacementRuleWorkspaceService
    {
        private readonly Dictionary<Guid, RegexReplacementRuleEditorModel> _editors;
        private readonly Dictionary<Guid, bool> _enabled;
        private List<Guid> _orderedRuleIds;

        public FakeRegexReplacementRuleWorkspaceService(params RegexReplacementRuleEditorModel[] editors)
        {
            _editors = editors.ToDictionary(editor => editor.Id!.Value);
            _enabled = editors.ToDictionary(editor => editor.Id!.Value, _ => true);
            _orderedRuleIds = editors.Select(editor => editor.Id!.Value).ToList();
        }

        public int SaveEditorCallCount { get; private set; }

        public Exception? SaveException { get; set; }
        public Exception? SetEnabledException { get; set; }
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
            throw new NotSupportedException();

        public Task<RuleJsonImportResult> ImportJsonAsync(string json, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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

        public Task SetRuleEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken)
        {
            if (SetEnabledException is not null)
            {
                throw SetEnabledException;
            }

            _enabled[ruleId] = isEnabled;
            return Task.CompletedTask;
        }

        public Task SaveOrderAsync(IReadOnlyList<Guid> orderedRuleIds, CancellationToken cancellationToken)
        {
            _orderedRuleIds = orderedRuleIds.ToList();
            return Task.CompletedTask;
        }

        public Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken)
        {
            _editors.Remove(ruleId);
            _enabled.Remove(ruleId);
            _orderedRuleIds.Remove(ruleId);
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
        public AppConfirmationDecision DeletionDecision { get; set; } = AppConfirmationDecision.Cancel;

        public ProjectedUiError Project(Exception exception) => new("操作失败。", UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            LastProjectedTitle = title;
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(
            string title,
            string message,
            CancellationToken cancellationToken) => Task.FromResult(DeletionDecision);
    }

    private sealed class FakeNavigationService : ITestNavigationService
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

    private sealed class FakePlaybackCoordinator : IPlaybackRegexReplacementRefresher
    {
        public int RegexRefreshCount { get; private set; }
        public Exception? RefreshException { get; set; }

        public PlaybackSnapshot CurrentSnapshot => PlaybackSnapshot.Idle;

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken)
        {
            RegexRefreshCount++;
            if (RefreshException is not null)
            {
                throw RefreshException;
            }

            return Task.CompletedTask;
        }
        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
