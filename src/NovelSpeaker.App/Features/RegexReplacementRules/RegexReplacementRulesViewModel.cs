using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Features.RuleEditing;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
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
    private readonly EditorSession<Guid?, RegexReplacementRuleEditorModel> _editorSession = new(EditorsEqual);
    private bool _loading;

    public RegexReplacementRulesViewModel(
        IRegexReplacementRuleWorkspaceService workspace,
        IPlaybackRegexReplacementRefresher playback,
        IAppFeedbackService feedback,
        IAppDialogService dialogs,
        IAppNavigator navigator)
    {
        _workspace = workspace;
        _playback = playback;
        _feedback = feedback;
        _dialogs = dialogs;
        _navigator = navigator;
    }

    public ObservableCollection<RegexReplacementRuleListItemViewModel> Rules { get; } = [];

    [ObservableProperty] private bool isBusy;
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
    public bool CanSave => HasEditor && !IsBusy && string.IsNullOrEmpty(ValidationMessage) && (HasUnsavedChanges || IsEditingNewRule);
    public bool CanDelete => HasEditor && !IsEditingNewRule && SelectedRuleId is not null && !IsBusy;

    public async Task LoadAsync(CancellationToken cancellationToken) => await RefreshAsync(SelectedRuleId, !HasEditor, cancellationToken);

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
        if (rule is null || IsBusy) return;
        var old = rule.IsEnabled;
        rule.IsEnabled = !old;
        try
        {
            IsBusy = true;
            await _workspace.SetRuleEnabledAsync(rule.Id, rule.IsEnabled, cancellationToken);
            await RefreshAsync(SelectedRuleId, false, cancellationToken);
            await _playback.RefreshRegexReplacementAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            rule.IsEnabled = old;
            _feedback.ShowProjectedNotification("保存启用状态失败", _feedback.Project(exception));
        }
        finally { IsBusy = false; NotifyCommandState(); }
    }

    [RelayCommand] private Task MoveUpAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken) => MoveAsync(rule, -1, cancellationToken);
    [RelayCommand] private Task MoveDownAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken) => MoveAsync(rule, 1, cancellationToken);

    public async Task ReorderByDropAsync(RegexReplacementRuleListItemViewModel? source, RegexReplacementRuleListItemViewModel? target, CancellationToken cancellationToken)
    {
        if (source is null || target is null || source.Id == target.Id || IsBusy) return;
        var ids = Rules.Select(rule => rule.Id).ToList();
        var sourceIndex = ids.IndexOf(source.Id);
        var targetIndex = ids.IndexOf(target.Id);
        if (sourceIndex < 0 || targetIndex < 0) return;
        ids.RemoveAt(sourceIndex);
        ids.Insert(targetIndex, source.Id);
        await SaveOrderAsync(ids, cancellationToken);
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await SaveCoreAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task CancelAsync(CancellationToken cancellationToken)
    {
        if (!HasEditor) return;
        await DiscardDraftAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedRuleId is not Guid id || !CanDelete) return;
        if (!await ConfirmLeaveAsync(cancellationToken)) return;
        var item = Rules.FirstOrDefault(rule => rule.Id == id);
        if (item is null || await _feedback.ConfirmDeletionAsync("删除正则替换规则", $"将删除规则“{item.Name}”。此操作不可撤销。", cancellationToken) != AppConfirmationDecision.Confirm) return;

        var fallback = GetAdjacentRuleId(id);
        try
        {
            IsBusy = true;
            await _workspace.DeleteRuleAsync(id, cancellationToken);
            CloseEditor();
            await RefreshAsync(fallback, true, cancellationToken);
            await _playback.RefreshRegexReplacementAsync(cancellationToken);
            _feedback.ShowSuccess("正则替换规则已删除", item.Name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _feedback.ShowProjectedNotification("删除正则替换规则失败", _feedback.Project(exception));
        }
        finally { IsBusy = false; NotifyCommandState(); }
    }

    partial void OnDraftNameChanged(string value) => Changed();
    partial void OnDraftPatternChanged(string value) => Changed();
    partial void OnDraftReplacementChanged(string value) => Changed();
    partial void OnDraftScopeChanged(RegexReplacementScope value) => Changed();
    partial void OnIsBusyChanged(bool value) => NotifyCommandState();
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
        finally { IsBusy = false; NotifyCommandState(); }
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
        OnPropertyChanged(nameof(CanDelete));
    }
}
