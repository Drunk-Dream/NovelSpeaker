using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Features.RuleEditing;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.App.Features.RegexReplacementRules;

/// <summary>UI workspace for global runtime regex replacement rules.</summary>
public sealed partial class RegexReplacementRulesViewModel : ObservableObject
{
    private readonly IRegexReplacementRuleWorkspaceService _workspace;
    private readonly IPlaybackRegexReplacementRefresher _playback;
    private readonly IAppFeedbackService _feedback;
    private readonly IAppDialogService _dialogs;
    private readonly IAppNavigator _navigator;
    private readonly IRuleDocumentInteraction _ruleDocuments;
    private readonly EditorSession<Guid?, RegexReplacementRuleEditorModel> _editorSession = new(EditorsEqual);
    private int _importOperationActive;
    private bool _loading;

    public RegexReplacementRulesViewModel(
        IRegexReplacementRuleWorkspaceService workspace,
        IPlaybackRegexReplacementRefresher playback,
        IAppFeedbackService feedback,
        IAppDialogService dialogs,
        IAppNavigator navigator,
        IRuleDocumentInteraction ruleDocuments)
    {
        _workspace = workspace;
        _playback = playback;
        _feedback = feedback;
        _dialogs = dialogs;
        _navigator = navigator;
        _ruleDocuments = ruleDocuments;
    }

    public ObservableCollection<RegexReplacementRuleListItemViewModel> Rules { get; } = [];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isHelpDrawerOpen;
    [ObservableProperty] private Guid? selectedRuleId;
    [ObservableProperty] private string draftName = string.Empty;
    [ObservableProperty] private string draftPattern = string.Empty;
    [ObservableProperty] private string draftReplacement = string.Empty;
    [ObservableProperty] private RegexReplacementScope draftScope = RegexReplacementScope.Both;
    [ObservableProperty] private string validationMessage = string.Empty;

    public bool HasEditor => _editorSession.HasEditor;
    public bool IsEditingNewRule => _editorSession.IsNew;
    public bool HasUnsavedChanges => _editorSession.IsDirty;
    public Array Scopes => Enum.GetValues(typeof(RegexReplacementScope));
    public string NameValidationMessage =>
        string.Equals(ValidationMessage, "规则名称不能为空。", StringComparison.Ordinal)
            ? ValidationMessage
            : string.Empty;
    public string PatternValidationMessage =>
        string.IsNullOrEmpty(NameValidationMessage)
            ? ValidationMessage
            : string.Empty;
    public bool CanSave => HasEditor && HasUnsavedChanges && !IsBusy && string.IsNullOrEmpty(ValidationMessage);
    public bool CanCancel => HasEditor && !IsBusy;

    public async Task LoadAsync(CancellationToken cancellationToken) => await RefreshAsync(SelectedRuleId, false, cancellationToken);

    public void HandleNavigatedFrom()
    {
        IsHelpDrawerOpen = false;
        ClearDragTarget();
    }

    public void SetDragTarget(RegexReplacementRuleListItemViewModel? targetRule)
    {
        foreach (var rule in Rules)
        {
            rule.IsDropTarget = targetRule is not null && rule.Id == targetRule.Id;
        }
    }

    public void ClearDragTarget()
    {
        foreach (var rule in Rules)
        {
            rule.IsDropTarget = false;
        }
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken)) return;
        if (!await _navigator.GoBackAsync(cancellationToken).ConfigureAwait(true))
        {
            await _navigator.NavigateAsync(AppRoutes.ImportTextSettings, cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task NewRuleAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken)) return;
        Open(new RegexReplacementRuleEditorModel(null, "新建规则", string.Empty, string.Empty, RegexReplacementScope.Both), true, SelectedRuleId);
    }

    public Task ImportRuleFileAsync(CancellationToken cancellationToken) =>
        ImportDocumentAsync(() => _ruleDocuments.PickImportAsync(cancellationToken), "正则替换规则导入失败", cancellationToken);

    public Task ImportRulesFromClipboardAsync(CancellationToken cancellationToken) =>
        ImportDocumentAsync(
            () => _ruleDocuments.ReadClipboardAsync(cancellationToken),
            "从剪贴板导入正则替换规则失败",
            cancellationToken,
            warnWhenMissing: true);

    [RelayCommand]
    public async Task ExportRuleAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null) return;
        try
        {
            var json = await _workspace.ExportRuleJsonAsync(rule.Id, cancellationToken);
            if (json is null)
            {
                _feedback.ShowWarning("导出失败", "未找到要导出的正则替换规则。");
                return;
            }

            if (await _ruleDocuments.ExportAsync("regex-replacement-rule.json", json, cancellationToken))
            {
                _feedback.ShowSuccess("正则替换规则已导出", rule.Name);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _feedback.ShowProjectedNotification("正则替换规则导出失败", _feedback.Project(exception));
        }
    }

    [RelayCommand]
    public async Task CopyRuleAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null) return;
        try
        {
            var json = await _workspace.ExportRuleJsonAsync(rule.Id, cancellationToken);
            if (json is null)
            {
                _feedback.ShowWarning("复制失败", "未找到要复制的正则替换规则。");
                return;
            }

            await _ruleDocuments.CopyAsync(json, cancellationToken);
            _feedback.ShowSuccess("正则替换规则已复制", rule.Name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _feedback.ShowProjectedNotification("正则替换规则复制失败", _feedback.Project(exception));
        }
    }

    [RelayCommand]
    private async Task SelectRuleAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null || (!IsEditingNewRule && rule.Id == SelectedRuleId)) return;
        if (!await ConfirmLeaveAsync(cancellationToken)) return;
        await LoadEditorAsync(rule.Id, cancellationToken);
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null || !rule.CanQuickActions) return;
        var old = rule.IsEnabled;
        var persisted = false;
        rule.IsEnabled = !old;
        try
        {
            IsBusy = true;
            await _workspace.SetRuleEnabledAsync(rule.Id, rule.IsEnabled, cancellationToken);
            persisted = true;
            await RefreshAsync(SelectedRuleId, false, cancellationToken);
            await _playback.RefreshRegexReplacementAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!persisted)
            {
                rule.IsEnabled = old;
            }

            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!persisted)
            {
                rule.IsEnabled = old;
            }

            _feedback.ShowProjectedNotification("保存启用状态失败", _feedback.Project(exception));
        }
        finally { IsBusy = false; NotifyCommandState(); }
    }

    [RelayCommand] private Task MoveUpAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken) => MoveAsync(rule, -1, cancellationToken);
    [RelayCommand] private Task MoveDownAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken) => MoveAsync(rule, 1, cancellationToken);

    public async Task ReorderByDropAsync(RegexReplacementRuleListItemViewModel? source, RegexReplacementRuleListItemViewModel? target, CancellationToken cancellationToken)
    {
        await ReorderRuleCoreAsync(source, target, RuleDropPlacement.Before, cancellationToken);
    }

    [RelayCommand]
    private async Task ReorderRuleAsync(RuleReorderRequest? request, CancellationToken cancellationToken)
    {
        if (request?.Source is not RegexReplacementRuleListItemViewModel source ||
            request.Target is not RegexReplacementRuleListItemViewModel target)
        {
            return;
        }

        await ReorderRuleCoreAsync(source, target, request.Placement, cancellationToken);
    }

    private async Task ReorderRuleCoreAsync(
        RegexReplacementRuleListItemViewModel? source,
        RegexReplacementRuleListItemViewModel? target,
        RuleDropPlacement placement,
        CancellationToken cancellationToken)
    {
        if (source is null ||
            target is null ||
            placement == RuleDropPlacement.None ||
            source.Id == target.Id ||
            IsBusy)
        {
            ClearDragTarget();
            return;
        }

        var ids = Rules.Select(rule => rule.Id).ToList();
        var sourceIndex = ids.IndexOf(source.Id);
        var targetIndex = ids.IndexOf(target.Id);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            ClearDragTarget();
            return;
        }
        ids.RemoveAt(sourceIndex);
        targetIndex = ids.IndexOf(target.Id);
        var insertionIndex = placement == RuleDropPlacement.After ? targetIndex + 1 : targetIndex;
        ids.Insert(insertionIndex, source.Id);
        await SaveOrderAsync(ids, cancellationToken);
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await SaveCoreAsync(cancellationToken);
    }

    [RelayCommand]
    private Task CancelAsync(CancellationToken cancellationToken)
    {
        if (!HasEditor) return Task.CompletedTask;
        CloseEditor();
        return Task.CompletedTask;
    }

    public async Task DeleteRuleFromListAsync(
        RegexReplacementRuleListItemViewModel rule,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (!rule.CanDeleteAction) return;
        if (!await ConfirmLeaveAsync(cancellationToken)) return;
        var item = Rules.FirstOrDefault(candidate => candidate.Id == rule.Id);
        if (item is null || await _feedback.ConfirmDeletionAsync("删除正则替换规则", $"将删除规则“{item.Name}”。此操作不可撤销。", cancellationToken) != AppConfirmationDecision.Confirm) return;
        if (IsBusy) return;

        var deletingOpenEditor = !IsEditingNewRule && SelectedRuleId == item.Id;
        var preferredRuleId = deletingOpenEditor ? GetAdjacentRuleId(item.Id) : SelectedRuleId;
        try
        {
            IsBusy = true;
            await _workspace.DeleteRuleAsync(item.Id, cancellationToken);
            await RefreshAsync(preferredRuleId, deletingOpenEditor, cancellationToken);
            await _playback.RefreshRegexReplacementAsync(cancellationToken);
            _feedback.ShowSuccess("正则替换规则已删除", item.Name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _feedback.ShowProjectedNotification("删除正则替换规则失败", _feedback.Project(exception));
        }
        finally { IsBusy = false; NotifyCommandState(); }
    }

    [RelayCommand]
    private Task DeleteRuleAsync(
        RegexReplacementRuleListItemViewModel? rule,
        CancellationToken cancellationToken) =>
        rule is null
            ? Task.CompletedTask
            : DeleteRuleFromListAsync(rule, cancellationToken);

    partial void OnDraftNameChanged(string value) => Changed();
    partial void OnDraftPatternChanged(string value) => Changed();
    partial void OnDraftReplacementChanged(string value) => Changed();
    partial void OnDraftScopeChanged(RegexReplacementScope value) => Changed();
    partial void OnValidationMessageChanged(string value)
    {
        OnPropertyChanged(nameof(NameValidationMessage));
        OnPropertyChanged(nameof(PatternValidationMessage));
    }
    partial void OnIsBusyChanged(bool value)
    {
        UpdateRuleItemStates();
        NotifyCommandState();
    }
    partial void OnSelectedRuleIdChanged(Guid? value) => NotifyCommandState();

    private async Task<RegexReplacementRuleEditorModel?> SaveCoreAsync(CancellationToken cancellationToken)
    {
        Validate();
        if (!CanSave) return null;
        try
        {
            IsBusy = true;
            var previous = _editorSession.Baseline;
            var wasNew = _editorSession.IsNew;
            var saved = await _workspace.SaveEditorAsync(new RegexReplacementRuleEditorModel(SelectedRuleId, DraftName, DraftPattern, DraftReplacement, DraftScope), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshAsync(saved.Id, false, cancellationToken);
            Open(saved, false, saved.Id);
            if (wasNew || previous is null || ExecutionFieldsChanged(previous, saved))
            {
                await _playback.RefreshRegexReplacementAsync(cancellationToken);
            }

            _feedback.ShowSuccess("正则替换规则已保存", saved.Name);
            return saved;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _feedback.ShowProjectedNotification("保存正则替换规则失败", _feedback.Project(exception));
            return null;
        }
        finally { IsBusy = false; NotifyCommandState(); }
    }

    private async Task MoveAsync(RegexReplacementRuleListItemViewModel? rule, int delta, CancellationToken cancellationToken)
    {
        if (rule is null || IsBusy) return;
        var ids = Rules.Select(item => item.Id).ToList();
        var index = ids.IndexOf(rule.Id);
        var target = index + delta;
        if (target < 0 || target >= ids.Count) return;
        (ids[index], ids[target]) = (ids[target], ids[index]);
        await SaveOrderAsync(ids, cancellationToken);
    }

    public Task MoveRuleUpFromListAsync(
        RegexReplacementRuleListItemViewModel rule,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return MoveAsync(rule, -1, cancellationToken);
    }

    public Task MoveRuleDownFromListAsync(
        RegexReplacementRuleListItemViewModel rule,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return MoveAsync(rule, 1, cancellationToken);
    }

    [RelayCommand]
    private void OpenHelp() => IsHelpDrawerOpen = true;

    [RelayCommand]
    private void CloseHelp() => IsHelpDrawerOpen = false;

    private async Task SaveOrderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            await _workspace.SaveOrderAsync(orderedIds, cancellationToken);
            await RefreshAsync(SelectedRuleId, false, cancellationToken);
            await _playback.RefreshRegexReplacementAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RefreshAsync(SelectedRuleId, false, cancellationToken);
            _feedback.ShowProjectedNotification("保存排序失败", _feedback.Project(exception));
        }
        finally
        {
            ClearDragTarget();
            IsBusy = false;
            NotifyCommandState();
        }
    }

    private async Task ImportDocumentAsync(
        Func<Task<RuleImportDocument?>> readDocument,
        string failureTitle,
        CancellationToken cancellationToken,
        bool warnWhenMissing = false)
    {
        if (Interlocked.CompareExchange(ref _importOperationActive, 1, 0) != 0)
        {
            return;
        }

        var ownsBusy = false;
        try
        {
            var document = await readDocument();
            if (document is null)
            {
                if (warnWhenMissing)
                {
                    _feedback.ShowWarning("无法导入", "剪贴板中没有可导入的文本内容。");
                }

                return;
            }

            if (IsBusy)
            {
                return;
            }

            if (!await ConfirmLeaveAsync(cancellationToken))
            {
                return;
            }

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            ownsBusy = true;
            NotifyCommandState();
            var result = await _workspace.ImportJsonAsync(document.Json, cancellationToken);
            await RefreshAsync(SelectedRuleId, false, cancellationToken);
            await _playback.RefreshRegexReplacementAsync(cancellationToken);
            _feedback.ShowSuccess(
                "正则替换规则导入完成",
                $"{document.SourceDescription}：新增 {result.ImportedCount} 条，跳过重复 {result.SkippedCount} 条。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _feedback.ShowProjectedNotification(failureTitle, _feedback.Project(exception));
        }
        finally
        {
            if (ownsBusy)
            {
                IsBusy = false;
                NotifyCommandState();
            }

            Volatile.Write(ref _importOperationActive, 0);
        }
    }

    public async Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken)
    {
        if (!HasUnsavedChanges) return true;
        var decision = await _dialogs.ShowUnsavedChangesAsync(
            "未保存的修改",
            "当前正则替换规则有未保存的修改。要先保存再继续吗？",
            "保存",
            "放弃",
            "取消",
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return decision switch
        {
            UnsavedChangesDecision.Save => await SaveCoreAsync(cancellationToken) is not null,
            UnsavedChangesDecision.Discard => await DiscardDraftAndReturnAsync(cancellationToken),
            _ => false
        };
    }

    private async Task<bool> DiscardDraftAndReturnAsync(CancellationToken cancellationToken)
    {
        await DiscardDraftAsync(cancellationToken);
        return true;
    }

    private async Task DiscardDraftAsync(CancellationToken cancellationToken)
    {
        if (IsEditingNewRule)
        {
            if (_editorSession.FallbackId is Guid fallback) await LoadEditorAsync(fallback, cancellationToken);
            else CloseEditor();
            return;
        }

        if (_editorSession.Baseline is not null)
        {
            Open(_editorSession.Baseline, false, _editorSession.FallbackId);
        }
    }

    private void Changed()
    {
        if (_loading) return;
        Validate();
        _editorSession.UpdateDirty(BuildEditor());
        NotifyCommandState();
    }

    private void Validate()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(DraftName)) throw new ArgumentException("规则名称不能为空。");
            if (string.IsNullOrWhiteSpace(DraftPattern)) throw new ArgumentException("正则表达式不能为空。");
            _ = new Regex(DraftPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            ValidationMessage = string.Empty;
        }
        catch (ArgumentException exception) { ValidationMessage = exception.Message; }
    }

    private RegexReplacementRuleEditorModel BuildEditor() => new(SelectedRuleId, DraftName, DraftPattern, DraftReplacement, DraftScope);

    private async Task LoadEditorAsync(Guid id, CancellationToken cancellationToken)
    {
        var editor = await _workspace.GetEditorAsync(id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (editor is not null) Open(editor, false, id);
    }

    private void Open(RegexReplacementRuleEditorModel editor, bool isNew, Guid? fallback)
    {
        _loading = true;
        _editorSession.Open(editor.Id, editor, isNew, fallback);
        SelectedRuleId = editor.Id;
        DraftName = editor.Name;
        DraftPattern = editor.Pattern;
        DraftReplacement = editor.Replacement;
        DraftScope = editor.Scope;
        _loading = false;
        Validate();
        foreach (var rule in Rules) rule.IsSelected = rule.Id == editor.Id;
        UpdateRuleItemStates();
        NotifyCommandState();
    }

    private void CloseEditor()
    {
        _loading = true;
        _editorSession.Close();
        SelectedRuleId = null;
        DraftName = string.Empty;
        DraftPattern = string.Empty;
        DraftReplacement = string.Empty;
        DraftScope = RegexReplacementScope.Both;
        ValidationMessage = string.Empty;
        _loading = false;
        foreach (var rule in Rules) rule.IsSelected = false;
        UpdateRuleItemStates();
        NotifyCommandState();
    }

    private async Task RefreshAsync(Guid? preferred, bool selectFirst, CancellationToken cancellationToken)
    {
        var items = await _workspace.GetRulesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Rules.Clear();
        foreach (var item in items)
        {
            Rules.Add(new RegexReplacementRuleListItemViewModel(item.Id, item.Name, item.PatternSummary, item.IsEnabled, item.Scope, item.Id == SelectedRuleId, item.ErrorMessage));
        }

        if (selectFirst && !IsEditingNewRule)
        {
            var target = items.FirstOrDefault(item => item.Id == preferred) ?? items.FirstOrDefault();
            if (target is not null) await LoadEditorAsync(target.Id, cancellationToken);
            else CloseEditor();
        }

        UpdateRuleItemStates();
        NotifyCommandState();
    }

    private Guid? GetAdjacentRuleId(Guid id)
    {
        var index = Rules.ToList().FindIndex(rule => rule.Id == id);
        if (index < 0) return null;
        return index + 1 < Rules.Count ? Rules[index + 1].Id : index > 0 ? Rules[index - 1].Id : null;
    }

    private static bool ExecutionFieldsChanged(RegexReplacementRuleEditorModel before, RegexReplacementRuleEditorModel after) =>
        !string.Equals(before.Pattern, after.Pattern, StringComparison.Ordinal) ||
        !string.Equals(before.Replacement, after.Replacement, StringComparison.Ordinal) ||
        before.Scope != after.Scope;

    private static bool EditorsEqual(RegexReplacementRuleEditorModel left, RegexReplacementRuleEditorModel right) =>
        left.Id == right.Id &&
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        string.Equals(left.Pattern, right.Pattern, StringComparison.Ordinal) &&
        string.Equals(left.Replacement, right.Replacement, StringComparison.Ordinal) &&
        left.Scope == right.Scope;

    private void NotifyCommandState()
    {
        OnPropertyChanged(nameof(HasEditor));
        OnPropertyChanged(nameof(IsEditingNewRule));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanCancel));
    }

    private void UpdateRuleItemStates()
    {
        for (var index = 0; index < Rules.Count; index++)
        {
            var rule = Rules[index];
            rule.CanQuickActions = !IsBusy;
            rule.CanMoveUp = !IsBusy && index > 0;
            rule.CanMoveDown = !IsBusy && index < Rules.Count - 1;
        }
    }
}
