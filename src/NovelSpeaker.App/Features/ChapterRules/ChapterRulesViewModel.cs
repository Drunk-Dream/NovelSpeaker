using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Features.RuleEditing;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.ChapterRules;

/// <summary>
/// Drives the chapter-rule workspace, including list selection, editor drafts, and default-rule actions.
/// </summary>
public sealed partial class ChapterRulesViewModel : ObservableObject
{
    private readonly IChapterRuleWorkspaceService _workspaceService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly IAppNavigator _navigator;
    private readonly EditorSession<string?, ChapterRuleEditorModel> _editorSession = new(EditorsEqual);
    private bool _suppressDraftStateUpdates;

    public ChapterRulesViewModel(
        IChapterRuleWorkspaceService workspaceService,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        IAppNavigator navigator)
    {
        _workspaceService = workspaceService;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _navigator = navigator;
    }

    public ObservableCollection<ChapterRuleListItemViewModel> Rules { get; } = [];

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isHelpDrawerOpen;

    [ObservableProperty]
    private string? highlightedRuleId;

    [ObservableProperty]
    private string draftName = string.Empty;

    [ObservableProperty]
    private string draftPattern = string.Empty;

    [ObservableProperty]
    private bool draftIsBuiltIn;

    [ObservableProperty]
    private string nameValidationMessage = string.Empty;

    [ObservableProperty]
    private string patternValidationMessage = string.Empty;

    public bool HasEditor => _editorSession.HasEditor;

    public bool IsEditingNewRule => _editorSession.IsNew;

    public bool HasUnsavedChanges => _editorSession.IsDirty;

    public bool HasValidationErrors =>
        !string.IsNullOrWhiteSpace(NameValidationMessage) ||
        !string.IsNullOrWhiteSpace(PatternValidationMessage);

    public bool CanSaveDraft => HasEditor && !IsBusy && !HasValidationErrors && (HasUnsavedChanges || IsEditingNewRule);

    public bool CanCancelEditing => HasEditor && !IsBusy;

    public bool CanDeleteCurrentRule => HasEditor && !IsEditingNewRule && CurrentRuleId is not null && !DraftIsBuiltIn && !IsBusy;

    public string? CurrentRuleId => IsEditingNewRule ? null : _editorSession.EditorId;

    public string DeleteRestrictionMessage => DraftIsBuiltIn
        ? "内置规则不可删除，可禁用或恢复默认。"
        : string.Empty;

    public bool ShowDeleteRestrictionMessage => DraftIsBuiltIn;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await RefreshRulesAsync(HighlightedRuleId, openEditorIfNeeded: !HasEditor, cancellationToken);
    }

    public void HandleNavigatedFrom()
    {
        IsHelpDrawerOpen = false;
        ClearDragTarget();
    }

    public void SetDragTarget(ChapterRuleListItemViewModel? targetRule)
    {
        foreach (var rule in Rules)
        {
            rule.IsDropTarget = targetRule is not null &&
                                !string.Equals(rule.Id, HighlightedRuleId, StringComparison.Ordinal) &&
                                string.Equals(rule.Id, targetRule.Id, StringComparison.Ordinal);
        }
    }

    public void ClearDragTarget()
    {
        foreach (var rule in Rules)
        {
            rule.IsDropTarget = false;
        }
    }

    public async Task ReorderByDropAsync(
        ChapterRuleListItemViewModel? sourceRule,
        ChapterRuleListItemViewModel? targetRule,
        CancellationToken cancellationToken)
    {
        if (sourceRule is null ||
            targetRule is null ||
            string.Equals(sourceRule.Id, targetRule.Id, StringComparison.Ordinal) ||
            !sourceRule.CanQuickActions ||
            !targetRule.CanQuickActions)
        {
            ClearDragTarget();
            return;
        }

        var orderedIds = Rules.Select(rule => rule.Id).ToList();
        var sourceIndex = orderedIds.FindIndex(id => string.Equals(id, sourceRule.Id, StringComparison.Ordinal));
        var targetIndex = orderedIds.FindIndex(id => string.Equals(id, targetRule.Id, StringComparison.Ordinal));
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            ClearDragTarget();
            return;
        }

        orderedIds.RemoveAt(sourceIndex);
        orderedIds.Insert(targetIndex, sourceRule.Id);
        await SaveRuleOrderAsync(orderedIds, cancellationToken);
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        if (!await _navigator.GoBackAsync(cancellationToken).ConfigureAwait(true))
        {
            await _navigator.NavigateAsync(AppRoutes.Settings, cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task NewRuleAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        OpenEditor(CreateEmptyEditor(), true, HighlightedRuleId);
    }

    [RelayCommand]
    private async Task SelectRuleAsync(ChapterRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null)
        {
            return;
        }

        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        if (!IsEditingNewRule && string.Equals(HighlightedRuleId, rule.Id, StringComparison.Ordinal))
        {
            return;
        }

        await OpenSavedRuleAsync(rule.Id, cancellationToken);
    }

    [RelayCommand]
    private async Task SaveDraftAsync(CancellationToken cancellationToken)
    {
        await SaveDraftCoreAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task CancelEditingAsync(CancellationToken cancellationToken)
    {
        if (!HasEditor)
        {
            return;
        }

        if (IsEditingNewRule)
        {
            if (!string.IsNullOrWhiteSpace(_editorSession.FallbackId))
            {
                await OpenSavedRuleAsync(_editorSession.FallbackId, cancellationToken);
            }
            else
            {
                CloseEditor();
            }

            return;
        }

        if (_editorSession.Baseline is not null)
        {
            OpenEditor(_editorSession.Baseline, false, _editorSession.FallbackId);
        }
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(CancellationToken cancellationToken)
    {
        if (!CanDeleteCurrentRule || CurrentRuleId is not string ruleId)
        {
            return;
        }

        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        var currentRule = Rules.FirstOrDefault(rule => string.Equals(rule.Id, ruleId, StringComparison.Ordinal));
        if (currentRule is null)
        {
            return;
        }

        var confirmed = await _feedbackService.ConfirmDeletionAsync(
            "删除章节规则",
            $"将删除章节规则“{currentRule.Name}”。此操作不可撤销。",
            cancellationToken);
        if (confirmed != AppConfirmationDecision.Confirm)
        {
            return;
        }

        var fallbackRuleId = GetAdjacentRuleId(ruleId);

        try
        {
            SetBusy(true);
            await _workspaceService.DeleteRuleAsync(ruleId, cancellationToken);
            await RefreshRulesAsync(fallbackRuleId, openEditorIfNeeded: true, cancellationToken);
            _feedbackService.ShowSuccess("章节规则已删除", $"已删除规则：{currentRule.Name}。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleProjectedError("章节规则删除失败", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    private async Task ImportDefaultsAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        await ApplyDefaultsAsync(ChapterRuleDefaultsMode.ImportDefaults, cancellationToken);
    }

    [RelayCommand]
    private async Task RestoreDefaultsAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        var decision = await _dialogService.ShowConfirmationAsync(
            "恢复默认规则",
            "将重置所有内置章节规则，但不会删除自定义规则。确定继续吗？",
            "恢复",
            "取消",
            cancellationToken);
        if (decision != AppConfirmationDecision.Confirm)
        {
            return;
        }

        await ApplyDefaultsAsync(ChapterRuleDefaultsMode.RestoreDefaults, cancellationToken);
    }

    [RelayCommand]
    private async Task ToggleRuleEnabledAsync(ChapterRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null || !rule.CanQuickActions)
        {
            return;
        }

        var originalValue = rule.IsEnabled;
        rule.IsEnabled = !originalValue;

        try
        {
            SetBusy(true);
            await _workspaceService.SetRuleEnabledAsync(rule.Id, rule.IsEnabled, cancellationToken);
            await RefreshRulesAsync(null, openEditorIfNeeded: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            rule.IsEnabled = originalValue;
            HandleProjectedError("章节规则启用状态保存失败", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    private async Task MoveRuleUpAsync(ChapterRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null || !rule.CanMoveUp)
        {
            return;
        }

        var orderedIds = Rules.Select(item => item.Id).ToList();
        var index = orderedIds.FindIndex(id => string.Equals(id, rule.Id, StringComparison.Ordinal));
        if (index <= 0)
        {
            return;
        }

        (orderedIds[index - 1], orderedIds[index]) = (orderedIds[index], orderedIds[index - 1]);
        await SaveRuleOrderAsync(orderedIds, cancellationToken);
    }

    [RelayCommand]
    private async Task MoveRuleDownAsync(ChapterRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null || !rule.CanMoveDown)
        {
            return;
        }

        var orderedIds = Rules.Select(item => item.Id).ToList();
        var index = orderedIds.FindIndex(id => string.Equals(id, rule.Id, StringComparison.Ordinal));
        if (index < 0 || index >= orderedIds.Count - 1)
        {
            return;
        }

        (orderedIds[index], orderedIds[index + 1]) = (orderedIds[index + 1], orderedIds[index]);
        await SaveRuleOrderAsync(orderedIds, cancellationToken);
    }

    [RelayCommand]
    private void OpenHelp()
    {
        IsHelpDrawerOpen = true;
    }

    [RelayCommand]
    private void CloseHelp()
    {
        IsHelpDrawerOpen = false;
    }

    partial void OnDraftNameChanged(string value)
    {
        if (_suppressDraftStateUpdates)
        {
            return;
        }

        ValidateDraft();
        UpdateUnsavedChanges();
    }

    partial void OnDraftPatternChanged(string value)
    {
        if (_suppressDraftStateUpdates)
        {
            return;
        }

        ValidateDraft();
        UpdateUnsavedChanges();
    }

    private async Task ApplyDefaultsAsync(ChapterRuleDefaultsMode mode, CancellationToken cancellationToken)
    {
        var preferredRuleId = CurrentRuleId;

        try
        {
            SetBusy(true);
            var result = await _workspaceService.ApplyDefaultsAsync(mode, cancellationToken);
            await RefreshRulesAsync(preferredRuleId, openEditorIfNeeded: true, cancellationToken);
            _feedbackService.ShowSuccess(
                mode == ChapterRuleDefaultsMode.ImportDefaults ? "默认规则导入完成" : "默认规则已恢复",
                BuildDefaultsMessage(result));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleProjectedError(
                mode == ChapterRuleDefaultsMode.ImportDefaults ? "默认规则导入失败" : "恢复默认规则失败",
                exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshRulesAsync(string? preferredRuleId, bool openEditorIfNeeded, CancellationToken cancellationToken)
    {
        var rules = await _workspaceService.GetRulesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Rules.ReplaceWith(
            rules,
            rule => new ChapterRuleListItemViewModel(
                rule.Id,
                rule.Name,
                rule.PatternSummary,
                rule.IsEnabled,
                rule.IsBuiltIn,
                !IsEditingNewRule && string.Equals(HighlightedRuleId, rule.Id, StringComparison.Ordinal)));

        if (openEditorIfNeeded && !IsEditingNewRule)
        {
            var targetRuleId = rules.SelectByKeyOrFallback(preferredRuleId ?? HighlightedRuleId, rule => rule.Id)?.Id;
            if (!string.IsNullOrWhiteSpace(targetRuleId))
            {
                await OpenSavedRuleAsync(targetRuleId, cancellationToken);
                return;
            }

            CloseEditor();
            return;
        }

        if (!IsEditingNewRule &&
            HighlightedRuleId is not null &&
            Rules.All(rule => !string.Equals(rule.Id, HighlightedRuleId, StringComparison.Ordinal)))
        {
            CloseEditor();
            return;
        }

        UpdateRuleItemStates();
        NotifyUiStateChanged();
    }

    private async Task OpenSavedRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        var editor = await _workspaceService.GetEditorAsync(ruleId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (editor is null)
        {
            CloseEditor();
            return;
        }

        OpenEditor(editor, false, ruleId);
    }

    private void OpenEditor(ChapterRuleEditorModel editor, bool isNew, string? fallbackRuleId)
    {
        _editorSession.Open(isNew ? null : editor.Id, editor, isNew, fallbackRuleId);
        HighlightedRuleId = isNew ? null : editor.Id;

        _suppressDraftStateUpdates = true;
        DraftName = editor.Name;
        DraftPattern = editor.Pattern;
        DraftIsBuiltIn = editor.IsBuiltIn;
        _suppressDraftStateUpdates = false;

        ValidateDraft();
        UpdateUnsavedChanges();
        UpdateRuleItemStates();
        NotifyUiStateChanged();
    }

    private void CloseEditor()
    {
        _editorSession.Close();
        HighlightedRuleId = null;

        _suppressDraftStateUpdates = true;
        DraftName = string.Empty;
        DraftPattern = string.Empty;
        DraftIsBuiltIn = false;
        NameValidationMessage = string.Empty;
        PatternValidationMessage = string.Empty;
        _suppressDraftStateUpdates = false;

        UpdateRuleItemStates();
        NotifyUiStateChanged();
    }

    private ChapterRuleEditorModel CreateEmptyEditor()
    {
        return new ChapterRuleEditorModel(
            null,
            "新建规则",
            string.Empty,
            false,
            true);
    }

    public async Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken)
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var decision = await _dialogService.ShowUnsavedChangesAsync(
            "未保存的修改",
            "当前章节规则有未保存的修改。要先保存再继续吗？",
            "保存",
            "放弃",
            "取消",
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        switch (decision)
        {
            case UnsavedChangesDecision.Save:
                return await SaveDraftCoreAsync(cancellationToken) is not null;
            case UnsavedChangesDecision.Discard:
                await DiscardCurrentDraftAsync(cancellationToken);
                return true;
            default:
                return false;
        }
    }

    private async Task DiscardCurrentDraftAsync(CancellationToken cancellationToken)
    {
        if (IsEditingNewRule)
        {
            if (_editorSession.FallbackId is not null)
            {
                await OpenSavedRuleAsync(_editorSession.FallbackId, cancellationToken);
            }
            else
            {
                CloseEditor();
            }

            return;
        }

        if (_editorSession.Baseline is not null)
        {
            OpenEditor(_editorSession.Baseline, false, _editorSession.FallbackId);
        }
    }

    private async Task<ChapterRuleEditorModel?> SaveDraftCoreAsync(CancellationToken cancellationToken)
    {
        if (!HasEditor)
        {
            return null;
        }

        try
        {
            SetBusy(true);
            var savedEditor = await _workspaceService.SaveEditorAsync(BuildCurrentEditorModel(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshRulesAsync(savedEditor.Id, openEditorIfNeeded: false, cancellationToken);
            OpenEditor(savedEditor, false, savedEditor.Id);
            _feedbackService.ShowSuccess("章节规则已保存", $"已保存规则：{savedEditor.Name}。");
            return savedEditor;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleProjectedError("章节规则保存失败", exception);
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private ChapterRuleEditorModel BuildCurrentEditorModel()
    {
        return new ChapterRuleEditorModel(
            CurrentRuleId,
            DraftName,
            DraftPattern,
            DraftIsBuiltIn,
            !DraftIsBuiltIn);
    }

    private void ValidateDraft()
    {
        NameValidationMessage = string.IsNullOrWhiteSpace(DraftName)
            ? "规则名称不能为空。"
            : string.Empty;

        if (string.IsNullOrWhiteSpace(DraftPattern))
        {
            PatternValidationMessage = "正则表达式不能为空。";
        }
        else
        {
            try
            {
                _ = new Regex(DraftPattern.Trim(), RegexOptions.CultureInvariant);
                PatternValidationMessage = string.Empty;
            }
            catch (ArgumentException exception)
            {
                PatternValidationMessage = $"正则表达式无效：{exception.Message}";
            }
        }

        NotifyUiStateChanged();
    }

    private void UpdateUnsavedChanges()
    {
        _editorSession.UpdateDirty(BuildCurrentEditorModel());
        NotifyUiStateChanged();
    }

    private static bool EditorsEqual(ChapterRuleEditorModel left, ChapterRuleEditorModel right)
    {
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
               string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
               string.Equals(left.Pattern, right.Pattern, StringComparison.Ordinal) &&
               left.IsBuiltIn == right.IsBuiltIn &&
               left.CanDelete == right.CanDelete;
    }

    private async Task SaveRuleOrderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken)
    {
        ApplyRuleOrder(orderedIds);

        try
        {
            SetBusy(true);
            await _workspaceService.SaveOrderAsync(orderedIds, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RefreshRulesAsync(null, openEditorIfNeeded: false, cancellationToken);
            HandleProjectedError("章节规则排序保存失败", exception);
        }
        finally
        {
            SetBusy(false);
            ClearDragTarget();
        }
    }

    private void ApplyRuleOrder(IReadOnlyList<string> orderedIds)
    {
        var byId = Rules.ToDictionary(rule => rule.Id, StringComparer.Ordinal);
        var reordered = orderedIds
            .Where(id => byId.ContainsKey(id))
            .Select(id => byId[id])
            .ToList();
        if (reordered.Count != Rules.Count)
        {
            return;
        }

        Rules.ReplaceWith(reordered, rule => rule);
        UpdateRuleItemStates();
        NotifyUiStateChanged();
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        UpdateRuleItemStates();
        NotifyUiStateChanged();
    }

    private void UpdateRuleItemStates()
    {
        for (var index = 0; index < Rules.Count; index++)
        {
            var rule = Rules[index];
            rule.IsSelected = !IsEditingNewRule &&
                              HighlightedRuleId is not null &&
                              string.Equals(rule.Id, HighlightedRuleId, StringComparison.Ordinal);

            var canQuickActions = !IsBusy;
            rule.CanQuickActions = canQuickActions;
            rule.CanMoveUp = canQuickActions && index > 0;
            rule.CanMoveDown = canQuickActions && index < Rules.Count - 1;
        }
    }

    private string? GetAdjacentRuleId(string ruleId)
    {
        var index = Rules.ToList().FindIndex(rule => string.Equals(rule.Id, ruleId, StringComparison.Ordinal));
        if (index < 0)
        {
            return HighlightedRuleId;
        }

        if (index > 0)
        {
            return Rules[index - 1].Id;
        }

        if (index + 1 < Rules.Count)
        {
            return Rules[index + 1].Id;
        }

        return null;
    }

    private static string BuildDefaultsMessage(ChapterRuleDefaultsApplyResult result)
    {
        return $"新增 {result.AddedCount} 条，更新 {result.UpdatedCount} 条，保持不变 {result.UnchangedCount} 条。";
    }

    private void NotifyUiStateChanged()
    {
        OnPropertyChanged(nameof(HasEditor));
        OnPropertyChanged(nameof(IsEditingNewRule));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(CanSaveDraft));
        OnPropertyChanged(nameof(CanCancelEditing));
        OnPropertyChanged(nameof(CanDeleteCurrentRule));
        OnPropertyChanged(nameof(CurrentRuleId));
        OnPropertyChanged(nameof(DeleteRestrictionMessage));
        OnPropertyChanged(nameof(ShowDeleteRestrictionMessage));
    }

    private void HandleProjectedError(string title, Exception exception)
    {
        var projected = _feedbackService.Project(exception);
        _feedbackService.ShowProjectedNotification(title, projected);
    }
}
