using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.RuleEditing;
using NovelSpeaker.App.Features.Rules.Shared;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Shell.Activation;
using RegularExpression = System.Text.RegularExpressions.Regex;

namespace NovelSpeaker.App.Features.Rules.Chapter;

/// <summary>
/// Drives the chapter-rule workspace, including list selection, editor drafts, and default-rule actions.
/// </summary>
public sealed partial class ChapterRulesViewModel : ObservableObject, ITransientEscapeHandler
{
    private readonly IChapterRuleWorkspaceService _workspaceService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly IAppNavigator _navigator;
    private readonly IRuleDocumentInteraction _ruleDocuments;
    private readonly EditorSession<string?, ChapterRuleEditorModel> _editorSession = new(EditorsEqual);
    private readonly ManagementSelectionController<string> _selection = new();
    private readonly RuleImportSession _importSession = new();
    private readonly ResettableObservableCollection<ChapterRuleListItemViewModel> _rules = [];
    private bool _suppressDraftStateUpdates;

    private PageActivationScope? _activation;
    private readonly BatchDeleteSession _batchDelete = new();
    private readonly WorkbenchExchangeInteraction _exchange;

    public ChapterRulesViewModel(
        IChapterRuleWorkspaceService workspaceService,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        IAppNavigator navigator,
        IRuleDocumentInteraction ruleDocuments)
    {
        _workspaceService = workspaceService;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _navigator = navigator;
        _ruleDocuments = ruleDocuments;
        _exchange = new(_ruleDocuments);
        _selection.StateChanged += (_, _) =>
        {
            UpdateRuleItemStates();
            OnPropertyChanged(nameof(IsManagementMode));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(CanReorder));
        };
    }

    public ObservableCollection<ChapterRuleListItemViewModel> Rules => _rules;

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

    public bool CanSaveDraft => HasEditor && HasUnsavedChanges && !IsBusy && !HasValidationErrors;

    public bool CanCancelEditing => HasEditor && !IsBusy;

    public string? CurrentRuleId => IsEditingNewRule ? null : _editorSession.EditorId;

    public bool IsManagementMode => _selection.IsManagementMode;
    public int SelectedCount => _selection.SelectedCount;
    public bool CanReorder => !IsBusy && !IsManagementMode;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_activation?.IsCurrent != true)
        {
            HandleNavigatedTo(new PageActivationController().Activate());
            _activation!.Register(cancellationToken.Register(_activation.Dispose));
        }
        await RefreshRulesAsync(HighlightedRuleId, openEditorIfNeeded: false, cancellationToken);
    }

    public void HandleNavigatedFrom()
    {
        var activation = _activation;
        _activation = null;
        activation?.Dispose();
        _selection.Reset();
        IsHelpDrawerOpen = false;
        ClearDragTarget();
    }

    public bool TryHandleEscape()
    {
        if (_selection.Exit()) return true;
        if (!IsHelpDrawerOpen)
        {
            return false;
        }

        IsHelpDrawerOpen = false;
        return true;
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

    public Task ReorderByDropAsync(ChapterRuleListItemViewModel? source, ChapterRuleListItemViewModel? target, CancellationToken cancellationToken) =>
        ReorderRuleCoreAsync(source, target is null ? -1 : Rules.IndexOf(target), cancellationToken);

    [RelayCommand]
    private Task ReorderRuleAsync(RuleReorderRequest? request, CancellationToken cancellationToken) =>
        ReorderRuleCoreAsync(request?.Source as ChapterRuleListItemViewModel, request?.SlotIndex ?? -1, cancellationToken);

    private async Task ReorderRuleCoreAsync(
        ChapterRuleListItemViewModel? source,
        int slotIndex,
        CancellationToken cancellationToken)
    {
        if (source is null || !source.CanQuickActions || IsBusy)
        {
            ClearDragTarget();
            return;
        }

        var order = Rules.Select(rule => rule.Id).ToArray();
        if (!RuleReorderController.TryMoveToSlot(order, order, source.Id, slotIndex, out var ids))
        {
            ClearDragTarget();
            return;
        }

        await SaveRuleOrderAsync(ids, cancellationToken);
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        await _navigator.NavigateBackAsync(cancellationToken, bypassGuard: true).ConfigureAwait(true);
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

    public Task ImportRuleFileAsync(CancellationToken cancellationToken) =>
        ImportDocumentAsync(
            token => _ruleDocuments.PickImportAsync(token),
            "章节规则导入失败",
            cancellationToken);

    public Task ImportRulesFromClipboardAsync(CancellationToken cancellationToken) =>
        ImportDocumentAsync(
            token => _ruleDocuments.ReadClipboardAsync(token),
            "从剪贴板导入章节规则失败",
            cancellationToken,
            warnWhenMissing: true);

    [RelayCommand]
    public Task ExportRuleAsync(ChapterRuleListItemViewModel? rule, CancellationToken cancellationToken) =>
        ExchangeRuleAsync(rule, false, cancellationToken);

    [RelayCommand]
    public Task CopyRuleAsync(ChapterRuleListItemViewModel? rule, CancellationToken cancellationToken) =>
        ExchangeRuleAsync(rule, true, cancellationToken);

    private async Task ExchangeRuleAsync(ChapterRuleListItemViewModel? rule, bool clipboard, CancellationToken cancellationToken)
    {
        if ((!IsManagementMode && rule is null) || (IsManagementMode && SelectedCount == 0)) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _activation?.CancellationToken ?? CancellationToken.None);
        var action = clipboard ? "复制" : "导出";
        try
        {
            var ids = RulesForExchange(rule);
            var successMessage = IsManagementMode ? $"成功 {ids.Count}，跳过 0，失败 0。" : $"已{action}规则：{rule!.Name}。";
            var result = await _exchange.WriteAsync(
                token => _workspaceService.ExportRulesJsonAsync(ids, token), clipboard, "chapter-rule.json", linked.Token);
            if (result == ExchangeWriteResult.MissingDocument)
                _feedbackService.ShowWarning($"{action}失败", $"未找到要{action}的章节规则。");
            else if (result == ExchangeWriteResult.Completed)
                _feedbackService.ShowSuccess($"章节规则已{action}", successMessage);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OperationCanceledException && !linked.IsCancellationRequested)
        {
            _feedbackService.ShowProjectedNotification($"章节规则{action}失败", _feedbackService.Project(exception));
        }
        catch (Exception) when (linked.IsCancellationRequested) { }
    }

    private IReadOnlyList<string> RulesForExchange(ChapterRuleListItemViewModel? rule) =>
        IsManagementMode ? _selection.SelectedItems.ToArray() : rule is null ? [] : [rule.Id];

    public async Task SelectRuleWithModifiersAsync(ChapterRuleListItemViewModel? rule,
        DesktopSelectionModifiers modifiers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (rule is null || IsBusy) return;
        if (IsManagementMode || modifiers != DesktopSelectionModifiers.None)
        {
            if (!IsManagementMode && !await TryEnterManagementAsync(cancellationToken)) return;
            _selection.HandleClick(rule.Id, modifiers);
            return;
        }
        await SelectRuleAsync(rule, cancellationToken);
    }

    [RelayCommand]
    private async Task SelectRuleAsync(ChapterRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null || IsBusy) return;
        if (_selection.HandleClick(rule.Id)) return;
        if (!IsEditingNewRule && HighlightedRuleId == rule.Id)
        {
            UpdateRuleItemStates();
            return;
        }
        if (!await ConfirmLeaveAsync(cancellationToken)) return;
        await OpenSavedRuleAsync(rule.Id, cancellationToken);
    }

    [RelayCommand]
    private async Task SaveDraftAsync(CancellationToken cancellationToken)
    {
        await SaveDraftCoreAsync(cancellationToken);
    }

    [RelayCommand]
    private Task CancelEditingAsync(CancellationToken cancellationToken)
    {
        if (!HasEditor)
        {
            return Task.CompletedTask;
        }

        CloseEditor();
        return Task.CompletedTask;
    }

    public async Task DeleteRuleFromListAsync(
        ChapterRuleListItemViewModel rule,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (IsManagementMode)
        {
            await DeleteSelectedRulesAsync(cancellationToken);
            return;
        }
        if (!rule.CanDeleteAction)
        {
            return;
        }

        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        var currentRule = Rules.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, rule.Id, StringComparison.Ordinal));
        if (currentRule is null || !currentRule.CanDelete)
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

        if (IsBusy)
        {
            return;
        }

        var deletingOpenEditor = !IsEditingNewRule &&
                                 string.Equals(CurrentRuleId, currentRule.Id, StringComparison.Ordinal);
        var preferredRuleId = deletingOpenEditor
            ? GetAdjacentRuleId(currentRule.Id)
            : CurrentRuleId;

        try
        {
            SetBusy(true);
            await _workspaceService.DeleteRuleAsync(currentRule.Id, cancellationToken);
            await RefreshRulesAsync(
                preferredRuleId,
                openEditorIfNeeded: deletingOpenEditor,
                cancellationToken);
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
    private Task DeleteRuleAsync(
        ChapterRuleListItemViewModel? rule,
        CancellationToken cancellationToken) =>
        rule is null
            ? Task.CompletedTask
            : DeleteRuleFromListAsync(rule, cancellationToken);

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
        catch (OperationCanceledException)
        {
            rule.IsEnabled = originalValue;
            throw;
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
    private Task MoveRuleUpAsync(ChapterRuleListItemViewModel? rule, CancellationToken cancellationToken) =>
        MoveRuleAsync(rule, -1, cancellationToken);

    public Task MoveRuleUpFromListAsync(ChapterRuleListItemViewModel rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return MoveRuleAsync(rule, -1, cancellationToken);
    }

    [RelayCommand]
    private Task MoveRuleDownAsync(ChapterRuleListItemViewModel? rule, CancellationToken cancellationToken) =>
        MoveRuleAsync(rule, 1, cancellationToken);

    public Task MoveRuleDownFromListAsync(ChapterRuleListItemViewModel rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return MoveRuleAsync(rule, 1, cancellationToken);
    }

    private async Task MoveRuleAsync(ChapterRuleListItemViewModel? rule, int offset, CancellationToken cancellationToken)
    {
        if (rule is null || (offset < 0 ? !rule.CanMoveUp : !rule.CanMoveDown)) return;
        if (RuleReorderController.TryMoveByOffset(Rules.Select(item => item.Id).ToArray(),
                rule.Id, offset, out var orderedIds, StringComparer.Ordinal))
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

    partial void OnDraftNameChanged(string value) => UpdateDraftState();
    partial void OnDraftPatternChanged(string value) => UpdateDraftState();

    private void UpdateDraftState()
    {
        if (_suppressDraftStateUpdates) return;
        ValidateDraft();
        UpdateUnsavedChanges();
    }

    private async Task ApplyDefaultsAsync(ChapterRuleDefaultsMode mode, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

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

    private async Task ImportDocumentAsync(
        Func<CancellationToken, Task<RuleImportDocument?>> readDocument,
        string failureTitle,
        CancellationToken cancellationToken,
        bool warnWhenMissing = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _activation?.CancellationToken ?? CancellationToken.None);
        cancellationToken = linked.Token;
        try
        {
            var execution = await _importSession.RunAsync(
                readDocument,
                ImportJsonAsyncCore,
                () => IsBusy,
                SetBusy,
                cancellationToken,
                reportMissingDocument: warnWhenMissing
                    ? () => _feedbackService.ShowWarning("无法导入", "剪贴板中没有可导入的文本内容。")
                    : null);
            if (execution is null)
            {
                return;
            }
            _feedbackService.ShowSuccess(
                "章节规则导入完成",
                $"{execution.Document.SourceDescription}：新增 {execution.Result.ImportedCount} 条，跳过重复 {execution.Result.SkippedCount} 条，失败 {execution.Result.FailedCount} 条。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            HandleProjectedError(failureTitle, exception);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task<RuleImportResult> ImportJsonAsyncCore(
        RuleImportDocument document,
        CancellationToken cancellationToken)
    {
        var result = await _workspaceService.ImportJsonAsync(document.Json, cancellationToken);
        await RefreshRulesAsync(HighlightedRuleId, openEditorIfNeeded: false, cancellationToken);
        return new RuleImportResult(result.ImportedCount, result.SkippedCount, result.TotalCount, result.FailedCount);
    }

    private async Task RefreshRulesAsync(string? preferredRuleId, bool openEditorIfNeeded, CancellationToken cancellationToken)
    {
        var rules = await _workspaceService.GetRulesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _selection.SetItems(rules.Select(rule => rule.Id));
        _rules.ReplaceWith(
            rules,
            rule => new ChapterRuleListItemViewModel(
                rule.Id,
                rule.Name,
                rule.PatternSummary,
                rule.IsEnabled,
                rule.IsBuiltIn,
                rule.CanDelete,
                _selection.IsSelected(rule.Id)));

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

    public Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken) =>
        _editorSession.ConfirmLeaveAsync(
            token => _dialogService.ShowUnsavedChangesAsync(
                "未保存的修改", "当前章节规则有未保存的修改。要先保存再继续吗？",
                "保存", "放弃", "取消", token),
            async token => await SaveDraftCoreAsync(token) is not null,
            DiscardCurrentDraftAsync, cancellationToken);

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
        if (!HasEditor || IsBusy)
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
                _ = new RegularExpression(DraftPattern.Trim(), RegexOptions.CultureInvariant, ChapterRuleRegexPolicy.MatchTimeout);
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
        var originalOrder = Rules.Select(rule => rule.Id).ToArray();
        ApplyRuleOrder(orderedIds);

        try
        {
            SetBusy(true);
            await _workspaceService.SaveOrderAsync(orderedIds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ApplyRuleOrder(originalOrder);
            throw;
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

        _rules.ReplaceWith(reordered, rule => rule);
        _selection.SetItems(Rules.Select(rule => rule.Id));
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
            rule.IsManagementMode = IsManagementMode;
            rule.IsSelected = IsManagementMode ? _selection.IsSelected(rule.Id) : rule.Id == CurrentRuleId;

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
        OnPropertyChanged(nameof(CanReorder));
        OnPropertyChanged(nameof(HasEditor));
        OnPropertyChanged(nameof(IsEditingNewRule));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(CanSaveDraft));
        OnPropertyChanged(nameof(CanCancelEditing));
        OnPropertyChanged(nameof(CurrentRuleId));
    }

    private void HandleProjectedError(string title, Exception exception)
    {
        var projected = _feedbackService.Project(exception);
        _feedbackService.ShowProjectedNotification(title, projected);
    }

    public void HandleNavigatedTo(PageActivationScope activation)
    {
        HandleNavigatedFrom();
        _activation = activation;
        activation.Register(() =>
        {
            if (ReferenceEquals(_activation, activation)) HandleNavigatedFrom();
        });
    }

    [RelayCommand]
    private async Task EnterManagementAsync(CancellationToken cancellationToken) =>
        _ = await TryEnterManagementAsync(cancellationToken);

    private async Task<bool> TryEnterManagementAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _activation?.CancellationToken ?? CancellationToken.None);
        return await _selection.TryEnterAsync(() => IsBusy, ConfirmLeaveAsync, linked.Token);
    }

    [RelayCommand]
    private void CancelManagement() => _selection.Exit();

    [RelayCommand]
    private void SelectAll() => _selection.SelectAll();

    public void HandleRuleRightClick(ChapterRuleListItemViewModel item) => _selection.HandleRightClick(item.Id);

    [RelayCommand]
    private async Task DeleteSelectedRulesAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || !IsManagementMode || SelectedCount == 0) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _activation?.CancellationToken ?? CancellationToken.None);
        try
        {
            var result = await _batchDelete.RunAsync<ChapterRuleListItemViewModel>(
                async token =>
                {
                    if (!await ConfirmLeaveAsync(token)) return [];
                    var items = Rules.Where(item => _selection.IsSelected(item.Id)).ToArray();
                    if (items.Length == 0) return [];
                    return await _feedbackService.ConfirmDeletionAsync("删除规则", $"将删除所选 {items.Length} 条规则，此操作不可撤销。", token)
                        == AppConfirmationDecision.Confirm ? items : [];
                },
                async (item, token) =>
                {
                    if (!item.CanDelete) return false;
                    await _workspaceService.DeleteRuleAsync(item.Id, token);
                    return true;
                },
                token => RefreshRulesAsync(CurrentRuleId, openEditorIfNeeded: false, token),
                SetBusy, linked.Token);
            if (result is null) return;
            var message = $"成功 {result.Succeeded}，跳过 {result.Skipped}，失败 {result.Failed}。";
            if (result.Skipped > 0 || result.Failed > 0) _feedbackService.ShowWarning("删除完成", message);
            else _feedbackService.ShowSuccess("删除完成", message);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception exception) when (!linked.IsCancellationRequested)
        {
            _feedbackService.ShowProjectedNotification("批量删除失败", _feedbackService.Project(exception));
        }
        catch (Exception) when (linked.IsCancellationRequested) { }
    }
}
