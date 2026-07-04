using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Pages;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Drives the chapter-rule workspace, including list selection, editor drafts, and default-rule actions.
/// </summary>
public sealed partial class ChapterRulesViewModel : ObservableObject
{
    private readonly IChapterRuleWorkspaceService _workspaceService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly INavigationService _navigationService;
    private ChapterRuleEditorModel? _baselineEditor;
    private string? _fallbackRuleId;
    private bool _suppressDraftStateUpdates;

    public ChapterRulesViewModel(
        IChapterRuleWorkspaceService workspaceService,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        INavigationService navigationService)
    {
        _workspaceService = workspaceService;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _navigationService = navigationService;
    }

    public ObservableCollection<ChapterRuleListItemViewModel> Rules { get; } = [];

    [ObservableProperty]
    private bool hasEditor;

    [ObservableProperty]
    private bool isEditingNewRule;

    [ObservableProperty]
    private bool hasUnsavedChanges;

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
    private bool draftIsEnabled = true;

    [ObservableProperty]
    private bool draftIsBuiltIn;

    [ObservableProperty]
    private string nameValidationMessage = string.Empty;

    [ObservableProperty]
    private string patternValidationMessage = string.Empty;

    public bool HasValidationErrors =>
        !string.IsNullOrWhiteSpace(NameValidationMessage) ||
        !string.IsNullOrWhiteSpace(PatternValidationMessage);

    public bool CanSaveDraft => HasEditor && !IsBusy && !HasValidationErrors && (HasUnsavedChanges || IsEditingNewRule);

    public bool CanCancelEditing => HasEditor && !IsBusy;

    public bool CanDeleteCurrentRule => HasEditor && !IsEditingNewRule && CurrentRuleId is not null && !DraftIsBuiltIn && !IsBusy;

    public string? CurrentRuleId => IsEditingNewRule ? null : _baselineEditor?.Id;

    public string DeleteRestrictionMessage => DraftIsBuiltIn
        ? "内置规则不可删除，可禁用或恢复默认。"
        : string.Empty;

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
        if (!await ConfirmLeaveDraftAsync(cancellationToken))
        {
            return;
        }

        if (!_navigationService.GoBack())
        {
            _navigationService.NavigateWithHierarchy(typeof(SettingsPage));
        }
    }

    [RelayCommand]
    private async Task NewRuleAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveDraftAsync(cancellationToken))
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

        if (!await ConfirmLeaveDraftAsync(cancellationToken))
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
            if (!string.IsNullOrWhiteSpace(_fallbackRuleId))
            {
                await OpenSavedRuleAsync(_fallbackRuleId, cancellationToken);
            }
            else
            {
                CloseEditor();
            }

            return;
        }

        if (_baselineEditor is not null)
        {
            OpenEditor(_baselineEditor, false, _fallbackRuleId);
        }
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(CancellationToken cancellationToken)
    {
        if (!CanDeleteCurrentRule || CurrentRuleId is not string ruleId)
        {
            return;
        }

        if (!await ConfirmLeaveDraftAsync(cancellationToken))
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
        catch (Exception exception)
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
        if (!await ConfirmLeaveDraftAsync(cancellationToken))
        {
            return;
        }

        await ApplyDefaultsAsync(ChapterRuleDefaultsMode.ImportDefaults, cancellationToken);
    }

    [RelayCommand]
    private async Task RestoreDefaultsAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveDraftAsync(cancellationToken))
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
        catch (Exception exception)
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

    partial void OnDraftIsEnabledChanged(bool value)
    {
        if (_suppressDraftStateUpdates)
        {
            return;
        }

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
        catch (Exception exception)
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
        if (editor is null)
        {
            CloseEditor();
            return;
        }

        OpenEditor(editor, false, ruleId);
    }

    private void OpenEditor(ChapterRuleEditorModel editor, bool isNew, string? fallbackRuleId)
    {
        _baselineEditor = editor;
        _fallbackRuleId = fallbackRuleId;
        HasEditor = true;
        IsEditingNewRule = isNew;
        HighlightedRuleId = isNew ? null : editor.Id;

        _suppressDraftStateUpdates = true;
        DraftName = editor.Name;
        DraftPattern = editor.Pattern;
        DraftIsEnabled = editor.IsEnabled;
        DraftIsBuiltIn = editor.IsBuiltIn;
        _suppressDraftStateUpdates = false;

        ValidateDraft();
        UpdateUnsavedChanges();
        UpdateRuleItemStates();
        NotifyUiStateChanged();
    }

    private void CloseEditor()
    {
        _baselineEditor = null;
        _fallbackRuleId = null;
        HasEditor = false;
        IsEditingNewRule = false;
        HighlightedRuleId = null;

        _suppressDraftStateUpdates = true;
        DraftName = string.Empty;
        DraftPattern = string.Empty;
        DraftIsEnabled = true;
        DraftIsBuiltIn = false;
        NameValidationMessage = string.Empty;
        PatternValidationMessage = string.Empty;
        _suppressDraftStateUpdates = false;

        HasUnsavedChanges = false;
        UpdateRuleItemStates();
        NotifyUiStateChanged();
    }

    private ChapterRuleEditorModel CreateEmptyEditor()
    {
        return new ChapterRuleEditorModel(
            null,
            "新建规则",
            string.Empty,
            true,
            false,
            true);
    }

    private async Task<bool> ConfirmLeaveDraftAsync(CancellationToken cancellationToken)
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
            if (_fallbackRuleId is not null)
            {
                await OpenSavedRuleAsync(_fallbackRuleId, cancellationToken);
            }
            else
            {
                CloseEditor();
            }

            return;
        }

        if (_baselineEditor is not null)
        {
            OpenEditor(_baselineEditor, false, _fallbackRuleId);
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
            await RefreshRulesAsync(savedEditor.Id, openEditorIfNeeded: false, cancellationToken);
            OpenEditor(savedEditor, false, savedEditor.Id);
            _feedbackService.ShowSuccess("章节规则已保存", $"已保存规则：{savedEditor.Name}。");
            return savedEditor;
        }
        catch (Exception exception)
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
            DraftIsEnabled,
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
        HasUnsavedChanges = HasEditor &&
                            _baselineEditor is not null &&
                            !EditorsEqual(_baselineEditor, BuildCurrentEditorModel());
        NotifyUiStateChanged();
    }

    private static bool EditorsEqual(ChapterRuleEditorModel left, ChapterRuleEditorModel right)
    {
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
               string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
               string.Equals(left.Pattern, right.Pattern, StringComparison.Ordinal) &&
               left.IsEnabled == right.IsEnabled &&
               left.IsBuiltIn == right.IsBuiltIn &&
               left.CanDelete == right.CanDelete;
    }

    private async Task SaveRuleOrderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken)
    {
        try
        {
            SetBusy(true);
            await _workspaceService.SaveOrderAsync(orderedIds, cancellationToken);
            await RefreshRulesAsync(null, openEditorIfNeeded: false, cancellationToken);
        }
        catch (Exception exception)
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

            var canQuickActions = !IsBusy && !rule.IsSelected;
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
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(CanSaveDraft));
        OnPropertyChanged(nameof(CanCancelEditing));
        OnPropertyChanged(nameof(CanDeleteCurrentRule));
        OnPropertyChanged(nameof(CurrentRuleId));
        OnPropertyChanged(nameof(DeleteRestrictionMessage));
    }

    private void HandleProjectedError(string title, Exception exception)
    {
        var projected = _feedbackService.Project(exception);
        _feedbackService.ShowProjectedNotification(title, projected);
    }
}
