using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.Domain.Books;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class RegexReplacementRulesViewModelTests
{
    [Theory]
    [InlineData(UnsavedChangesDecision.Save, true)]
    [InlineData(UnsavedChangesDecision.Discard, true)]
    [InlineData(UnsavedChangesDecision.Cancel, false)]
    public async Task ConfirmLeaveAsync_applies_global_navigation_decision(
        UnsavedChangesDecision decision,
        bool expectedCanLeave)
    {
        var fixture = CreateFixture(decision);
        await fixture.ViewModel.LoadAsync(CancellationToken.None);
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
        await fixture.ViewModel.LoadAsync(CancellationToken.None);
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
        await fixture.ViewModel.LoadAsync(CancellationToken.None);
        fixture.ViewModel.DraftPattern = "已修改";

        await fixture.ViewModel.SelectRuleCommand.ExecuteAsync(fixture.ViewModel.Rules[1]);

        Assert.Equal(fixture.FirstRuleId, fixture.ViewModel.SelectedRuleId);
        Assert.Equal("已修改", fixture.ViewModel.DraftPattern);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.Equal(0, fixture.Workspace.SaveEditorCallCount);
    }

    [Fact]
    public async Task SelectRuleAsync_save_failure_keeps_current_draft_and_blocks_leave()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        fixture.Workspace.SaveException = new InvalidOperationException("save failed");
        await fixture.ViewModel.LoadAsync(CancellationToken.None);
        fixture.ViewModel.DraftPattern = "已修改";

        await fixture.ViewModel.SelectRuleCommand.ExecuteAsync(fixture.ViewModel.Rules[1]);

        Assert.Equal(fixture.FirstRuleId, fixture.ViewModel.SelectedRuleId);
        Assert.Equal("已修改", fixture.ViewModel.DraftPattern);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.Equal("保存正则替换规则失败", fixture.Feedback.LastProjectedTitle);
    }

    [Fact]
    public async Task SaveAsync_refreshes_playback_when_execution_fields_change()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        await fixture.ViewModel.LoadAsync(CancellationToken.None);
        fixture.ViewModel.DraftReplacement = "新替换";

        await fixture.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Playback.RegexRefreshCount);
    }

    [Fact]
    public async Task SaveAsync_does_not_refresh_playback_when_only_name_changes()
    {
        var fixture = CreateFixture(UnsavedChangesDecision.Save);
        await fixture.ViewModel.LoadAsync(CancellationToken.None);
        fixture.ViewModel.DraftName = "新名称";

        await fixture.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, fixture.Playback.RegexRefreshCount);
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

        public FakeRegexReplacementRuleWorkspaceService(params RegexReplacementRuleEditorModel[] editors)
        {
            _editors = editors.ToDictionary(editor => editor.Id!.Value);
        }

        public int SaveEditorCallCount { get; private set; }

        public Exception? SaveException { get; set; }

        public Task<IReadOnlyList<RegexReplacementRuleListItem>> GetRulesAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<RegexReplacementRuleListItem> rules = _editors.Values
                .Select((editor, index) => new RegexReplacementRuleListItem(
                    editor.Id!.Value,
                    editor.Name,
                    editor.Pattern,
                    true,
                    (index + 1) * 10,
                    editor.Scope))
                .ToArray();
            return Task.FromResult(rules);
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
            return Task.FromResult(saved);
        }

        public Task SetRuleEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveOrderAsync(IReadOnlyList<Guid> orderedRuleIds, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken)
        {
            _editors.Remove(ruleId);
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
            CancellationToken cancellationToken) => Task.FromResult(AppConfirmationDecision.Cancel);
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
            return Task.CompletedTask;
        }
        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
