using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.PresentationTests.TestDoubles;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

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
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false, true)
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
        await LoadAndSelectAsync(viewModel, "custom:one");
        viewModel.DraftName = "已修改";

        var canLeave = await viewModel.ConfirmLeaveAsync(CancellationToken.None);

        Assert.Equal(expectedCanLeave, canLeave);
        Assert.Equal(!expectedCanLeave, viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task LoadAsync_leaves_editor_closed_until_a_rule_is_clicked()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("builtin:one", "内置规则", @"^\s*第一章$", true, 10, true, false),
            new ChapterRuleListItem("custom:two", "自定义规则", @"^\s*第二章$", true, 20, false, true)
        ])
        {
            EditorsById =
            {
                ["builtin:one"] = new ChapterRuleEditorModel("builtin:one", "内置规则", @"^\s*第一章$", true, false)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.HasEditor);
        Assert.Null(viewModel.HighlightedRuleId);
        Assert.All(viewModel.Rules, rule => Assert.False(rule.IsSelected));

        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules[0]);

        Assert.True(viewModel.HasEditor);
        Assert.Equal("builtin:one", viewModel.HighlightedRuleId);
    }

    [Fact]
    public async Task Clean_editor_allows_cancel_but_disables_save_until_draft_changes()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false, true)
        ])
        {
            EditorsById =
            {
                ["custom:one"] = new ChapterRuleEditorModel("custom:one", "规则一", @"^\s*一$", false, true)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);

        await LoadAndSelectAsync(viewModel, "custom:one");

        Assert.True(viewModel.CanCancelEditing);
        Assert.False(viewModel.CanSaveDraft);

        viewModel.DraftName = "规则一（修改）";

        Assert.True(viewModel.CanCancelEditing);
        Assert.True(viewModel.CanSaveDraft);

        await viewModel.CancelEditingCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasEditor);
        Assert.False(viewModel.CanCancelEditing);
        Assert.False(viewModel.CanSaveDraft);
    }

    [Fact]
    public async Task NewRuleAsync_saves_after_deduplication_and_selects_saved_rule()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:existing", "新建规则", @"^\s*旧规则$", true, 10, false, true)
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
    public async Task NewRuleAsync_tracks_dirty_and_cancel_closes_editor()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false, true)
        ])
        {
            EditorsById =
            {
                ["custom:one"] = new ChapterRuleEditorModel("custom:one", "规则一", @"^\s*一$", false, true)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.NewRuleCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEditingNewRule);
        Assert.False(viewModel.HasUnsavedChanges);
        viewModel.DraftPattern = @"^\s*新章节$";
        Assert.True(viewModel.HasUnsavedChanges);

        await viewModel.CancelEditingCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEditingNewRule);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.HasEditor);
        Assert.Null(viewModel.HighlightedRuleId);
    }

    [Fact]
    public async Task SelectRuleAsync_with_unsaved_changes_respects_discard()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false, true),
            new ChapterRuleListItem("custom:two", "规则二", @"^\s*二$", true, 20, false, true)
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
    public async Task SelectRuleAsync_save_failure_keeps_current_draft_and_blocks_selection()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false, true),
            new ChapterRuleListItem("custom:two", "规则二", @"^\s*二$", true, 20, false, true)
        ])
        {
            SaveException = new InvalidOperationException("save failed"),
            EditorsById =
            {
                ["custom:one"] = new ChapterRuleEditorModel("custom:one", "规则一", @"^\s*一$", false, true),
                ["custom:two"] = new ChapterRuleEditorModel("custom:two", "规则二", @"^\s*二$", false, true)
            }
        };
        var viewModel = CreateViewModel(
            workspaceService: workspace,
            dialogService: new FakeAppDialogService { NextUnsavedDecision = UnsavedChangesDecision.Save });
        await LoadAndSelectAsync(viewModel, "custom:one");
        viewModel.DraftName = "未保存名称";

        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules.Single(rule => rule.Id == "custom:two"));

        Assert.Equal("custom:one", viewModel.HighlightedRuleId);
        Assert.Equal("未保存名称", viewModel.DraftName);
        Assert.True(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task DraftValidation_disables_save_for_invalid_regex()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false, true)
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
    public async Task BuiltInRule_projects_nondelete_capability_in_list()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("builtin:one", "内置规则", @"^\s*一$", true, 10, true, false)
        ])
        {
            EditorsById =
            {
                ["builtin:one"] = new ChapterRuleEditorModel("builtin:one", "内置规则", @"^\s*一$", true, false)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.Rules.Single().CanDelete);
    }

    [Fact]
    public async Task DeleteRuleFromListAsync_deletes_nonselected_rule_without_switching_editor()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:selected", "选中规则", @"^\s*一$", true, 10, false, true),
            new ChapterRuleListItem("custom:delete", "待删除规则", @"^\s*二$", true, 20, false, true)
        ])
        {
            EditorsById =
            {
                ["custom:selected"] = new ChapterRuleEditorModel("custom:selected", "选中规则", @"^\s*一$", false, true)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);
        await LoadAndSelectAsync(viewModel, "custom:selected");

        await viewModel.DeleteRuleFromListAsync(
            viewModel.Rules.Single(rule => rule.Id == "custom:delete"),
            CancellationToken.None);

        Assert.Equal("custom:selected", viewModel.HighlightedRuleId);
        Assert.Equal("选中规则", viewModel.DraftName);
        Assert.DoesNotContain(viewModel.Rules, rule => rule.Id == "custom:delete");
    }

    [Fact]
    public async Task DeleteRuleFromListAsync_failure_keeps_list_and_editor()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:selected", "选中规则", @"^\s*一$", true, 10, false, true),
            new ChapterRuleListItem("custom:delete", "待删除规则", @"^\s*二$", true, 20, false, true)
        ])
        {
            ThrowOnDelete = true,
            EditorsById =
            {
                ["custom:selected"] = new ChapterRuleEditorModel("custom:selected", "选中规则", @"^\s*一$", false, true)
            }
        };
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(workspaceService: workspace, feedbackService: feedback);
        await LoadAndSelectAsync(viewModel, "custom:selected");

        await viewModel.DeleteRuleFromListAsync(
            viewModel.Rules.Single(rule => rule.Id == "custom:delete"),
            CancellationToken.None);

        Assert.Equal(["custom:selected", "custom:delete"], viewModel.Rules.Select(rule => rule.Id));
        Assert.Equal("custom:selected", viewModel.HighlightedRuleId);
        Assert.Equal("章节规则删除失败", feedback.LastTitle);
    }

    [Fact]
    public async Task ToggleRuleEnabledAsync_on_selected_rule_keeps_editor_draft_and_skips_unsaved_prompt()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:selected", "选中规则", @"^\s*一$", true, 10, false, true),
            new ChapterRuleListItem("custom:other", "其他规则", @"^\s*二$", true, 20, false, true)
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
        await LoadAndSelectAsync(viewModel, "custom:selected");
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
            new ChapterRuleListItem("builtin:selected", "选中规则", @"^\s*一$", true, 10, true, false),
            new ChapterRuleListItem("custom:toggle", "可切换规则", @"^\s*二$", true, 20, false, true)
        ])
        {
            SetRuleEnabledException = new InvalidOperationException("保存失败。"),
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
    public async Task ToggleRuleEnabledAsync_cancellation_rolls_back_list_state_and_propagates()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:selected", "选中规则", @"^\s*一$", true, 10, false, true)
        ])
        {
            SetRuleEnabledException = new OperationCanceledException(),
            EditorsById =
            {
                ["custom:selected"] = new ChapterRuleEditorModel("custom:selected", "选中规则", @"^\s*一$", false, true)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);
        await viewModel.LoadAsync(CancellationToken.None);
        var targetRule = viewModel.Rules.Single();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => viewModel.ToggleRuleEnabledCommand.ExecuteAsync(targetRule));

        Assert.True(targetRule.IsEnabled);
        Assert.True(targetRule.CanQuickActions);
    }

    [Fact]
    public async Task ReorderByDropAsync_failure_reloads_persisted_order()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("builtin:selected", "选中规则", @"^\s*一$", true, 10, true, false),
            new ChapterRuleListItem("custom:second", "第二条", @"^\s*二$", true, 20, false, true),
            new ChapterRuleListItem("custom:third", "第三条", @"^\s*三$", true, 30, false, true)
        ])
        {
            SaveOrderException = new InvalidOperationException("排序失败。"),
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
    public async Task ReorderByDropAsync_cancellation_restores_original_order_and_propagates()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:first", "第一条", @"^\s*一$", true, 10, false, true),
            new ChapterRuleListItem("custom:second", "第二条", @"^\s*二$", true, 20, false, true),
            new ChapterRuleListItem("custom:third", "第三条", @"^\s*三$", true, 30, false, true)
        ])
        {
            SaveOrderException = new OperationCanceledException(),
            EditorsById =
            {
                ["custom:first"] = new ChapterRuleEditorModel("custom:first", "第一条", @"^\s*一$", false, true)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);
        await viewModel.LoadAsync(CancellationToken.None);

        await Assert.ThrowsAsync<OperationCanceledException>(() => viewModel.ReorderByDropAsync(
            viewModel.Rules.Single(rule => rule.Id == "custom:third"),
            viewModel.Rules.Single(rule => rule.Id == "custom:second"),
            CancellationToken.None));

        Assert.Equal(
            ["custom:first", "custom:second", "custom:third"],
            viewModel.Rules.Select(rule => rule.Id));
        Assert.All(viewModel.Rules, rule => Assert.True(rule.CanQuickActions));
    }

    [Theory]
    [InlineData(RuleDropPlacement.Before, "custom:third", "custom:first", "custom:second")]
    [InlineData(RuleDropPlacement.After, "custom:first", "custom:third", "custom:second")]
    public async Task ReorderRuleCommand_honors_insertion_line_placement(
        RuleDropPlacement placement,
        string first,
        string second,
        string third)
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:first", "第一条", @"^\s*一$", true, 10, false, true),
            new ChapterRuleListItem("custom:second", "第二条", @"^\s*二$", true, 20, false, true),
            new ChapterRuleListItem("custom:third", "第三条", @"^\s*三$", true, 30, false, true)
        ]);
        var viewModel = CreateViewModel(workspaceService: workspace);
        await viewModel.LoadAsync(CancellationToken.None);
        var source = viewModel.Rules.Single(rule => rule.Id == "custom:third");
        var target = viewModel.Rules.Single(rule => rule.Id == "custom:first");

        await viewModel.ReorderRuleCommand.ExecuteAsync(new RuleReorderRequest(source, target, placement));

        Assert.Equal([first, second, third], viewModel.Rules.Select(rule => rule.Id));
    }

    [Fact]
    public async Task ImportDefaultsAsync_with_unsaved_changes_saves_first_then_applies_defaults()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false, true)
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
        await LoadAndSelectAsync(viewModel, "custom:one");
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
            new ChapterRuleListItem("custom:one", "规则一", @"^\s*一$", true, 10, false, true)
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
        await LoadAndSelectAsync(viewModel, "custom:one");
        viewModel.DraftPattern = @"^\s*已修改$";

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal(0, navigationService.GoBackCallCount);
    }

    [Fact]
    public async Task MoveRuleUpAndSaveDraftAsync_preserves_reordered_selection_and_skips_unsaved_prompt()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:first", "第一条", @"^\s*一$", true, 10, false, true),
            new ChapterRuleListItem("custom:selected", "第二条", @"^\s*二$", true, 20, false, true),
            new ChapterRuleListItem("custom:third", "第三条", @"^\s*三$", true, 30, false, true)
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
            new ChapterRuleListItem("custom:selected", "选中规则", @"^\s*一$", true, 10, false, true),
            new ChapterRuleListItem("custom:other", "其他规则", @"^\s*二$", true, 20, false, true)
        ])
        {
            EditorsById =
            {
                ["custom:selected"] = new ChapterRuleEditorModel("custom:selected", "选中规则", @"^\s*一$", false, true)
            }
        };
        var viewModel = CreateViewModel(workspaceService: workspace);
        await LoadAndSelectAsync(viewModel, "custom:selected");
        viewModel.DraftName = "选中规则-草稿";

        var selectedRule = viewModel.Rules.Single(rule => rule.Id == "custom:selected");
        await viewModel.ToggleRuleEnabledCommand.ExecuteAsync(selectedRule);
        await viewModel.CancelEditingCommand.ExecuteAsync(null);

        Assert.False(selectedRule.IsEnabled);
        Assert.False(viewModel.HasEditor);
        Assert.False(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task Shared_document_commands_import_export_and_copy_rule()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则", "^一$", true, 10, false, true)
        ]);
        var documents = new FakeRuleDocumentInteraction
        {
            ClipboardDocument = new RuleImportDocument("[{\"name\":\"导入\",\"pattern\":\"^二$\"}]", "剪贴板")
        };
        var viewModel = CreateViewModel(workspaceService: workspace, ruleDocuments: documents);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.ImportRulesFromClipboardAsync(CancellationToken.None);
        await viewModel.ExportRuleAsync(viewModel.Rules.Single(), CancellationToken.None);
        await viewModel.CopyRuleAsync(viewModel.Rules.Single(), CancellationToken.None);

        Assert.Equal(documents.ClipboardDocument.Json, workspace.LastImportedJson);
        Assert.Equal("chapter-rule.json", documents.ExportedFileName);
        Assert.Equal(workspace.ExportedJson, documents.ExportedJson);
        Assert.Equal(workspace.ExportedJson, documents.CopiedJson);
        Assert.False(viewModel.HasEditor);
    }

    [Fact]
    public async Task Missing_import_sources_preserve_dirty_draft_and_imports_do_not_overlap()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则", "^一$", true, 10, false, true)
        ])
        {
            EditorsById =
            {
                ["custom:one"] = new ChapterRuleEditorModel("custom:one", "规则", "^一$", false, true)
            }
        };
        var documents = new FakeRuleDocumentInteraction();
        var viewModel = CreateViewModel(workspaceService: workspace, ruleDocuments: documents);
        await LoadAndSelectAsync(viewModel, "custom:one");
        viewModel.DraftName = "未保存名称";

        await viewModel.ImportRuleFileAsync(CancellationToken.None);
        await viewModel.ImportRulesFromClipboardAsync(CancellationToken.None);

        Assert.True(viewModel.HasUnsavedChanges);
        Assert.Equal("未保存名称", viewModel.DraftName);

        var gate = new TaskCompletionSource<RuleImportDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        documents.FileDocumentGate = gate;
        var fileImport = viewModel.ImportRuleFileAsync(CancellationToken.None);
        await viewModel.ImportRulesFromClipboardAsync(CancellationToken.None);
        Assert.Equal(2, documents.FileReadCount);
        Assert.Equal(1, documents.ClipboardReadCount);
        gate.SetResult(null);
        await fileImport;
    }

    [Fact]
    public async Task Import_and_rule_mutations_share_busy_ownership()
    {
        var workspace = new FakeChapterRuleWorkspaceService(
        [
            new ChapterRuleListItem("custom:one", "规则", "^一$", false, 10, false, true)
        ])
        {
            SetEnabledGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var documents = new FakeRuleDocumentInteraction
        {
            FileDocument = new RuleImportDocument("[]", "rules.json")
        };
        var viewModel = CreateViewModel(workspaceService: workspace, ruleDocuments: documents);
        await viewModel.LoadAsync(CancellationToken.None);

        var toggle = viewModel.ToggleRuleEnabledCommand.ExecuteAsync(viewModel.Rules.Single());
        await workspace.SetEnabledEntered.Task;
        await viewModel.ImportRuleFileAsync(CancellationToken.None);
        Assert.Equal(0, workspace.ImportCallCount);
        Assert.True(viewModel.IsBusy);
        workspace.SetEnabledGate.SetResult();
        await toggle;

        workspace.ImportGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var import = viewModel.ImportRuleFileAsync(CancellationToken.None);
        await workspace.ImportEntered.Task;
        var enabledCalls = workspace.SetEnabledCallCount;
        await viewModel.ToggleRuleEnabledCommand.ExecuteAsync(viewModel.Rules.Single());
        Assert.Equal(enabledCalls, workspace.SetEnabledCallCount);
        Assert.True(viewModel.IsBusy);
        workspace.ImportGate.SetResult();
        await import;
    }

    private static ChapterRulesViewModel CreateViewModel(
        FakeChapterRuleWorkspaceService? workspaceService = null,
        FakeFeedbackService? feedbackService = null,
        FakeAppDialogService? dialogService = null,
        FakeNavigationService? navigationService = null,
        IRuleDocumentInteraction? ruleDocuments = null)
    {
        return new ChapterRulesViewModel(
            workspaceService ?? new FakeChapterRuleWorkspaceService([]),
            feedbackService ?? new FakeFeedbackService(),
            dialogService ?? new FakeAppDialogService(),
            navigationService ?? new FakeNavigationService(),
            ruleDocuments ?? new FakeRuleDocumentInteraction());
    }

    private static async Task LoadAndSelectAsync(ChapterRulesViewModel viewModel, string ruleId)
    {
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules.Single(rule => rule.Id == ruleId));
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

        public Exception? SetRuleEnabledException { get; set; }

        public TaskCompletionSource? SetEnabledGate { get; set; }

        public TaskCompletionSource SetEnabledEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SetEnabledCallCount { get; private set; }

        public TaskCompletionSource? ImportGate { get; set; }

        public TaskCompletionSource ImportEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ImportCallCount { get; private set; }

        public Exception? SaveOrderException { get; set; }

        public bool ThrowOnDelete { get; set; }

        public Exception? SaveException { get; set; }

        public int SaveEditorCallCount { get; private set; }

        public string? LastImportedJson { get; private set; }

        public string ExportedJson { get; set; } = """{"name":"规则"}""";

        public List<ChapterRuleDefaultsMode> AppliedDefaultModes { get; } = [];

        public Task<IReadOnlyList<ChapterRuleListItem>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChapterRuleListItem>>(_rules.OrderBy(rule => rule.SortOrder).ToArray());
        }

        public Task<string?> ExportRuleJsonAsync(string ruleId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(ExportedJson);

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

        public Task<ChapterRuleEditorModel?> GetEditorAsync(string ruleId, CancellationToken cancellationToken)
        {
            EditorsById.TryGetValue(ruleId, out var editor);
            return Task.FromResult(editor);
        }

        public Task<ChapterRuleEditorModel> SaveEditorAsync(ChapterRuleEditorModel editor, CancellationToken cancellationToken)
        {
            SaveEditorCallCount++;
            if (SaveException is not null)
            {
                throw SaveException;
            }

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
                false,
                true));
            NormalizeOrder();
            return Task.FromResult(savedEditor);
        }

        public Task DeleteRuleAsync(string ruleId, CancellationToken cancellationToken)
        {
            if (ThrowOnDelete)
            {
                throw new InvalidOperationException("删除失败。");
            }

            _rules.RemoveAll(rule => rule.Id == ruleId);
            EditorsById.Remove(ruleId);
            NormalizeOrder();
            return Task.CompletedTask;
        }

        public async Task SetRuleEnabledAsync(string ruleId, bool isEnabled, CancellationToken cancellationToken)
        {
            SetEnabledCallCount++;
            SetEnabledEntered.TrySetResult();
            if (SetEnabledGate is not null)
            {
                await SetEnabledGate.Task.WaitAsync(cancellationToken);
            }

            if (SetRuleEnabledException is not null)
            {
                throw SetRuleEnabledException;
            }

            var rule = _rules.Single(item => item.Id == ruleId);
            var index = _rules.IndexOf(rule);
            _rules[index] = rule with { IsEnabled = isEnabled };

        }

        public Task SaveOrderAsync(IReadOnlyList<string> orderedRuleIds, CancellationToken cancellationToken)
        {
            if (SaveOrderException is not null)
            {
                throw SaveOrderException;
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
                _rules.Add(new ChapterRuleListItem("builtin:imported", "导入默认规则", @"^\s*导入默认$", true, (_rules.Count + 1) * 10, true, false));
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
