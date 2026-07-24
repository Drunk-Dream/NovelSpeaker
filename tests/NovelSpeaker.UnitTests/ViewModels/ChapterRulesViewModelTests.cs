using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class ChapterRulesViewModelTests
{
    [Theory]
    [InlineData(UnsavedChangesDecision.Save, true)]
    [InlineData(UnsavedChangesDecision.Discard, true)]
    [InlineData(UnsavedChangesDecision.Cancel, false)]
    public async Task ConfirmLeaveAsync_applies_global_navigation_decision(
        UnsavedChangesDecision decision,
        bool expectedCanLeave)
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false)
        ])
        {
            EditorsById =
            {
                ["custom:one"] = new ChapterRuleEditorModel("custom:one", "规则一", @"^\s*一$", false, true)
            }
        };
        var viewModel = CreateViewModel(
            workspaceService: workspace,
            dialogService: new FakeAppDialogService { NextUnsavedDecision = decision });
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DraftName = "已修改";

        var canLeave = await viewModel.ConfirmLeaveAsync(CancellationToken.None);

        Assert.Equal(expectedCanLeave, canLeave);
        Assert.Equal(!expectedCanLeave, viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task LoadAsync_selects_first_rule_and_opens_editor()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("builtin:one", "内置规则", @"^\s*第一章$", true, 10, true),
            new ChapterRuleListItem("custom:two", "自定义规则", @"^\s*第二章$", true, 20, false)
        ])
        {
            EditorsById =
            {
                ["builtin:one"] = new ChapterRuleEditorModel("builtin:one", "内置规则", @"^\s*第一章$", true, false)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasEditor);
        Assert.Equal("builtin:one", viewModel.HighlightedRuleId);
        Assert.Equal("内置规则", viewModel.DraftName);
        Assert.True(viewModel.Rules.Single(rule => rule.Id == "builtin:one").IsSelected);
    }

    [Fact]
    public async Task NewRuleAsync_saves_after_deduplication_and_selects_saved_rule()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:existing", "新建规则", @"^\s*旧规则$", true, 10, false)
        ])
        {
            EditorsById =
            {
                ["custom:existing"] = new ChapterRuleEditorModel("custom:existing", "新建规则", @"^\s*旧规则$", false, true)
            }
        };
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(workspaceService: workspace, feedbackService: feedback);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.NewRuleCommand.ExecuteAsync(null);
        viewModel.DraftPattern = @"^\s*新章节$";
        await viewModel.SaveDraftCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Rules.Count);
        Assert.False(viewModel.IsEditingNewRule);
        Assert.Equal("新建规则(2)", viewModel.DraftName);
        Assert.Equal("章节规则已保存", feedback.LastTitle);
    }

    [Fact]
    public async Task SelectRuleAsync_with_unsaved_changes_respects_discard()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false),
            new ChapterRuleListItem("custom:two", "规则二", @"^\s*二$", true, 20, false)
        ])
        {
            EditorsById =
            {
                ["custom:one"] = new ChapterRuleEditorModel("custom:one", "规则一", @"^\s*一$", false, true),
                ["custom:two"] = new ChapterRuleEditorModel("custom:two", "规则二", @"^\s*二$", false, true)
            }
        };
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Discard
        };
        var viewModel = CreateViewModel(workspaceService: workspace, dialogService: dialogService);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DraftPattern = @"^\s*已修改$";

        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules.Single(rule => rule.Id == "custom:two"));

        Assert.Equal("custom:two", viewModel.HighlightedRuleId);
        Assert.Equal("规则二", viewModel.DraftName);
    }

    [Fact]
    public async Task DraftValidation_disables_save_for_invalid_regex()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false)
        ])
        {
            EditorsById =
            {
                ["custom:one"] = new ChapterRuleEditorModel("custom:one", "规则一", @"^\s*一$", false, true)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.DraftPattern = "[";

        Assert.False(string.IsNullOrWhiteSpace(viewModel.PatternValidationMessage));
        Assert.True(viewModel.HasValidationErrors);
        Assert.False(viewModel.CanSaveDraft);
    }

    [Fact]
    public async Task BuiltInRule_cannot_be_deleted_from_editor()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("builtin:one", "内置规则", @"^\s*一$", true, 10, true)
        ])
        {
            EditorsById =
            {
                ["builtin:one"] = new ChapterRuleEditorModel("builtin:one", "内置规则", @"^\s*一$", true, false)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.DraftIsBuiltIn);
        Assert.False(viewModel.CanDeleteCurrentRule);
        Assert.Equal("内置规则不可删除，可禁用或恢复默认。", viewModel.DeleteRestrictionMessage);
    }

    [Fact]
    public async Task ToggleRuleEnabledAsync_on_selected_rule_keeps_editor_draft_and_skips_unsaved_prompt()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:selected", "选中规则", @"^\s*一$", true, 10, false),
            new ChapterRuleListItem("custom:other", "其他规则", @"^\s*二$", true, 20, false)
        ])
        {
            EditorsById =
            {
                ["custom:selected"] = new ChapterRuleEditorModel("custom:selected", "选中规则", @"^\s*一$", false, true)
            }
        };
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Cancel
        };
        var viewModel = CreateViewModel(workspaceService: workspace, dialogService: dialogService);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DraftPattern = @"^\s*已修改$";

        var selectedRule = viewModel.Rules.Single(rule => rule.Id == "custom:selected");
        await viewModel.ToggleRuleEnabledCommand.ExecuteAsync(selectedRule);

        Assert.False(selectedRule.IsEnabled);
        Assert.Equal(@"^\s*已修改$", viewModel.DraftPattern);
        Assert.True(viewModel.HasUnsavedChanges);
        Assert.Equal(0, dialogService.UnsavedChangesPromptCount);
    }

    [Fact]
    public async Task ToggleRuleEnabledAsync_failure_rolls_back_list_state()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("builtin:selected", "选中规则", @"^\s*一$", true, 10, true),
            new ChapterRuleListItem("custom:toggle", "可切换规则", @"^\s*二$", true, 20, false)
        ])
        {
            ThrowOnSetRuleEnabled = true,
            EditorsById =
            {
                ["builtin:selected"] = new ChapterRuleEditorModel("builtin:selected", "选中规则", @"^\s*一$", true, false)
            }
        };
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(workspaceService: workspace, feedbackService: feedback);
        await viewModel.LoadAsync(CancellationToken.None);

        var targetRule = viewModel.Rules.Single(rule => rule.Id == "custom:toggle");
        await viewModel.ToggleRuleEnabledCommand.ExecuteAsync(targetRule);

        Assert.True(targetRule.IsEnabled);
        Assert.Equal("章节规则启用状态保存失败", feedback.LastTitle);
    }

    [Fact]
    public async Task ReorderByDropAsync_failure_reloads_persisted_order()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("builtin:selected", "选中规则", @"^\s*一$", true, 10, true),
            new ChapterRuleListItem("custom:second", "第二条", @"^\s*二$", true, 20, false),
            new ChapterRuleListItem("custom:third", "第三条", @"^\s*三$", true, 30, false)
        ])
        {
            ThrowOnSaveOrder = true,
            EditorsById =
            {
                ["builtin:selected"] = new ChapterRuleEditorModel("builtin:selected", "选中规则", @"^\s*一$", true, false)
            }
        };
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(workspaceService: workspace, feedbackService: feedback);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.ReorderByDropAsync(
            viewModel.Rules.Single(rule => rule.Id == "custom:third"),
            viewModel.Rules.Single(rule => rule.Id == "custom:second"),
            CancellationToken.None);

        Assert.Equal(["builtin:selected", "custom:second", "custom:third"], viewModel.Rules.Select(rule => rule.Id));
        Assert.Equal("章节规则排序保存失败", feedback.LastTitle);
    }

    [Fact]
    public async Task ImportDefaultsAsync_with_unsaved_changes_saves_first_then_applies_defaults()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false)
        ])
        {
            EditorsById =
            {
                ["custom:one"] = new ChapterRuleEditorModel("custom:one", "规则一", @"^\s*一$", false, true)
            }
        };
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Save
        };
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(workspaceService: workspace, dialogService: dialogService, feedbackService: feedback);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DraftPattern = @"^\s*已修改$";

        await viewModel.ImportDefaultsCommand.ExecuteAsync(null);

        Assert.Equal(1, workspace.SaveEditorCallCount);
        Assert.Equal([ChapterRuleDefaultsMode.ImportDefaults], workspace.AppliedDefaultModes);
        Assert.Equal("默认规则导入完成", feedback.LastTitle);
    }

    [Fact]
    public async Task BackAsync_cancelled_unsaved_changes_does_not_navigate()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false)
        ])
        {
            EditorsById =
            {
                ["custom:one"] = new ChapterRuleEditorModel("custom:one", "规则一", @"^\s*一$", false, true)
            }
        };
        var navigationService = new FakeNavigationService();
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Cancel
        };
        var viewModel = CreateViewModel(
            workspaceService: workspace,
            dialogService: dialogService,
            navigationService: navigationService);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DraftPattern = @"^\s*已修改$";

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal(0, navigationService.GoBackCallCount);
    }

    [Fact]
    public async Task MoveRuleUpAndSaveDraftAsync_preserves_reordered_selection_and_skips_unsaved_prompt()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:first", "第一条", @"^\s*一$", true, 10, false),
            new ChapterRuleListItem("custom:selected", "第二条", @"^\s*二$", true, 20, false),
            new ChapterRuleListItem("custom:third", "第三条", @"^\s*三$", true, 30, false)
        ])
        {
            EditorsById =
            {
                ["custom:first"] = new ChapterRuleEditorModel("custom:first", "第一条", @"^\s*一$", false, true),
                ["custom:selected"] = new ChapterRuleEditorModel("custom:selected", "第二条", @"^\s*二$", false, true)
            }
        };
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Cancel
        };
        var viewModel = CreateViewModel(workspaceService: workspace, dialogService: dialogService);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules.Single(rule => rule.Id == "custom:selected"));
        viewModel.DraftName = "第二条-已改";
        await viewModel.MoveRuleUpCommand.ExecuteAsync(viewModel.Rules.Single(rule => rule.Id == "custom:selected"));
        await viewModel.SaveDraftCommand.ExecuteAsync(null);

        Assert.Equal(["custom:selected", "custom:first", "custom:third"], viewModel.Rules.Select(rule => rule.Id));
        Assert.Equal("custom:selected", viewModel.HighlightedRuleId);
        Assert.Equal("第二条-已改", workspace.EditorsById["custom:selected"].Name);
        Assert.Equal(0, dialogService.UnsavedChangesPromptCount);
    }

    [Fact]
    public async Task CancelEditingAsync_does_not_rollback_left_saved_enabled_state()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:selected", "选中规则", @"^\s*一$", true, 10, false),
            new ChapterRuleListItem("custom:other", "其他规则", @"^\s*二$", true, 20, false)
        ])
        {
            EditorsById =
            {
                ["custom:selected"] = new ChapterRuleEditorModel("custom:selected", "选中规则", @"^\s*一$", false, true)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DraftName = "选中规则-草稿";

        var selectedRule = viewModel.Rules.Single(rule => rule.Id == "custom:selected");
        await viewModel.ToggleRuleEnabledCommand.ExecuteAsync(selectedRule);
        await viewModel.CancelEditingCommand.ExecuteAsync(null);

        Assert.False(selectedRule.IsEnabled);
        Assert.Equal("选中规则", viewModel.DraftName);
        Assert.False(viewModel.HasUnsavedChanges);
    }

    private static ChapterRulesViewModel CreateViewModel(
        FakeChapterRuleWorkspaceService? workspaceService = null,
        FakeFeedbackService? feedbackService = null,
        FakeAppDialogService? dialogService = null,
        FakeNavigationService? navigationService = null)
    {
        return new ChapterRulesViewModel(
            workspaceService ?? new FakeChapterRuleWorkspaceService([]),
            feedbackService ?? new FakeFeedbackService(),
            dialogService ?? new FakeAppDialogService(),
            navigationService ?? new FakeNavigationService());
    }

    private sealed class FakeChapterRuleWorkspaceService : IChapterRuleWorkspaceService
    {
        private readonly List<ChapterRuleListItem> _rules;
        private int _nextCustomId = 1;

        public FakeChapterRuleWorkspaceService(IReadOnlyList<ChapterRuleListItem> rules)
        {
            _rules = rules.OrderBy(rule => rule.SortOrder).ToList();
        }

        public Dictionary<string, ChapterRuleEditorModel> EditorsById { get; } = [];

        public bool ThrowOnSetRuleEnabled { get; set; }

        public bool ThrowOnSaveOrder { get; set; }

        public int SaveEditorCallCount { get; private set; }

        public List<ChapterRuleDefaultsMode> AppliedDefaultModes { get; } = [];

        public Task<IReadOnlyList<ChapterRuleListItem>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChapterRuleListItem>>(_rules.OrderBy(rule => rule.SortOrder).ToArray());
        }

        public Task<ChapterRuleEditorModel?> GetEditorAsync(string ruleId, CancellationToken cancellationToken)
        {
            EditorsById.TryGetValue(ruleId, out var editor);
            return Task.FromResult(editor);
        }

        public Task<ChapterRuleEditorModel> SaveEditorAsync(ChapterRuleEditorModel editor, CancellationToken cancellationToken)
        {
            SaveEditorCallCount++;
            if (editor.Pattern == "[")
            {
                throw new InvalidOperationException("正则表达式无效。");
            }

            var id = editor.Id ?? $"custom:new-{_nextCustomId++}";
            var savedName = BuildDeduplicatedName(editor.Name.Trim(), id);
            var existingRule = _rules.FirstOrDefault(rule => rule.Id == id);
            var savedEditor = new ChapterRuleEditorModel(
                id,
                savedName,
                editor.Pattern.Trim(),
                false,
                true);

            EditorsById[id] = savedEditor;
            _rules.RemoveAll(rule => rule.Id == id);
            _rules.Add(new ChapterRuleListItem(
                id,
                savedName,
                editor.Pattern.Trim(),
                existingRule?.IsEnabled ?? true,
                existingRule?.SortOrder ?? (_rules.Count + 1) * 10,
                false));
            NormalizeOrder();
            return Task.FromResult(savedEditor);
        }

        public Task DeleteRuleAsync(string ruleId, CancellationToken cancellationToken)
        {
            _rules.RemoveAll(rule => rule.Id == ruleId);
            EditorsById.Remove(ruleId);
            NormalizeOrder();
            return Task.CompletedTask;
        }

        public Task SetRuleEnabledAsync(string ruleId, bool isEnabled, CancellationToken cancellationToken)
        {
            if (ThrowOnSetRuleEnabled)
            {
                throw new InvalidOperationException("保存失败。");
            }

            var rule = _rules.Single(item => item.Id == ruleId);
            var index = _rules.IndexOf(rule);
            _rules[index] = rule with { IsEnabled = isEnabled };

            return Task.CompletedTask;
        }

        public Task SaveOrderAsync(IReadOnlyList<string> orderedRuleIds, CancellationToken cancellationToken)
        {
            if (ThrowOnSaveOrder)
            {
                throw new InvalidOperationException("排序失败。");
            }

            var reordered = orderedRuleIds
                .Select((id, index) => _rules.Single(rule => rule.Id == id) with { SortOrder = (index + 1) * 10 })
                .ToList();
            _rules.Clear();
            _rules.AddRange(reordered);
            return Task.CompletedTask;
        }

        public Task<ChapterRuleDefaultsApplyResult> ApplyDefaultsAsync(ChapterRuleDefaultsMode mode, CancellationToken cancellationToken)
        {
            AppliedDefaultModes.Add(mode);
            if (_rules.All(rule => rule.Id != "builtin:imported"))
            {
                _rules.Add(new ChapterRuleListItem("builtin:imported", "导入默认规则", @"^\s*导入默认$", true, (_rules.Count + 1) * 10, true));
                EditorsById["builtin:imported"] = new ChapterRuleEditorModel("builtin:imported", "导入默认规则", @"^\s*导入默认$", true, false);
                NormalizeOrder();
            }

            return Task.FromResult(new ChapterRuleDefaultsApplyResult(mode, 1, 0, 0));
        }

        private string BuildDeduplicatedName(string requestedName, string currentId)
        {
            if (_rules.All(rule => rule.Id == currentId || !string.Equals(rule.Name, requestedName, StringComparison.Ordinal)))
            {
                return requestedName;
            }

            for (var suffix = 2; suffix < 100; suffix++)
            {
                var candidate = $"{requestedName}({suffix})";
                if (_rules.All(rule => rule.Id == currentId || !string.Equals(rule.Name, candidate, StringComparison.Ordinal)))
                {
                    return candidate;
                }
            }

            return requestedName;
        }

        private void NormalizeOrder()
        {
            var normalized = _rules
                .OrderBy(rule => rule.SortOrder)
                .ThenBy(rule => rule.Name, StringComparer.Ordinal)
                .Select((rule, index) => rule with { SortOrder = (index + 1) * 10 })
                .ToList();
            _rules.Clear();
            _rules.AddRange(normalized);
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public string? LastTitle { get; private set; }

        public string? LastMessage { get; private set; }

        public AppConfirmationDecision NextConfirmationDecision { get; set; } = AppConfirmationDecision.Confirm;

        public ProjectedUiError Project(Exception exception)
        {
            return new ExceptionProjector().Project(exception);
        }

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
            LastTitle = title;
            LastMessage = message;
            return Task.FromResult(NextConfirmationDecision);
        }
    }

    private sealed class FakeAppDialogService : IAppDialogService
    {
        public UnsavedChangesDecision NextUnsavedDecision { get; set; } = UnsavedChangesDecision.Discard;

        public AppConfirmationDecision NextConfirmationDecision { get; set; } = AppConfirmationDecision.Confirm;

        public int UnsavedChangesPromptCount { get; private set; }

        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NextConfirmationDecision);
        }

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(
            string title,
            string message,
            string saveButtonText,
            string discardButtonText,
            string cancelButtonText,
            CancellationToken cancellationToken)
        {
            UnsavedChangesPromptCount++;
            return Task.FromResult(NextUnsavedDecision);
        }
    }

    private sealed class FakeNavigationService : ITestNavigationService
    {
        public int GoBackCallCount { get; private set; }

        public INavigationView GetNavigationControl() => throw new NotSupportedException();

        public bool GoBack()
        {
            GoBackCallCount++;
            return true;
        }

        public bool Navigate(Type pageType) => true;
        public bool Navigate(Type pageType, object? dataContext) => true;
        public bool Navigate(string pageIdOrTargetTag) => true;
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;
        public bool NavigateWithHierarchy(Type pageType) => true;
        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => true;
        public void SetNavigationControl(INavigationView navigation) { }
    }
}
