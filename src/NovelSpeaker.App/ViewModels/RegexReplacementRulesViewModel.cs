using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Pages;
using NovelSpeaker.Domain.Books;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

/// <summary>UI workspace for global runtime regex replacement rules.</summary>
public sealed partial class RegexReplacementRulesViewModel : ObservableObject
{
    private readonly IRegexReplacementRuleWorkspaceService _workspace;
    private readonly IPlaybackCoordinator _playback;
    private readonly IAppFeedbackService _feedback;
    private readonly INavigationService _navigation;
    private RegexReplacementRuleEditorModel? _baseline;
    private bool _loading;

    public RegexReplacementRulesViewModel(IRegexReplacementRuleWorkspaceService workspace, IPlaybackCoordinator playback, IAppFeedbackService feedback, INavigationService navigation)
    { _workspace = workspace; _playback = playback; _feedback = feedback; _navigation = navigation; }

    public ObservableCollection<RegexReplacementRuleListItemViewModel> Rules { get; } = [];
    [ObservableProperty] private bool hasEditor;
    [ObservableProperty] private bool isEditingNewRule;
    [ObservableProperty] private bool hasUnsavedChanges;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private Guid? selectedRuleId;
    [ObservableProperty] private string draftName = string.Empty;
    [ObservableProperty] private string draftPattern = string.Empty;
    [ObservableProperty] private string draftReplacement = string.Empty;
    [ObservableProperty] private RegexReplacementScope draftScope = RegexReplacementScope.Both;
    [ObservableProperty] private string validationMessage = string.Empty;
    public Array Scopes => Enum.GetValues(typeof(RegexReplacementScope));
    public bool CanSave => HasEditor && !IsBusy && string.IsNullOrEmpty(ValidationMessage) && (HasUnsavedChanges || IsEditingNewRule);
    public bool CanDelete => HasEditor && !IsEditingNewRule && SelectedRuleId is not null && !IsBusy;

    public async Task LoadAsync(CancellationToken cancellationToken) => await RefreshAsync(SelectedRuleId, !HasEditor, cancellationToken);

    [RelayCommand] private void Back() { if (!_navigation.GoBack()) _navigation.NavigateWithHierarchy(typeof(ImportTextSettingsPage)); }
    [RelayCommand] private void NewRule() => Open(new RegexReplacementRuleEditorModel(null, string.Empty, string.Empty, string.Empty, RegexReplacementScope.Both), true);
    [RelayCommand]
    private async Task SelectRuleAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken)
    { if (rule is not null && (!HasUnsavedChanges || rule.Id == SelectedRuleId)) await LoadEditorAsync(rule.Id, cancellationToken); }
    [RelayCommand]
    private async Task ToggleEnabledAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null) return; var old = rule.IsEnabled; rule.IsEnabled = !old;
        try { await _workspace.SetRuleEnabledAsync(rule.Id, rule.IsEnabled, cancellationToken); await RefreshAsync(SelectedRuleId, false, cancellationToken); await _playback.RefreshRegexReplacementAsync(cancellationToken); }
        catch (Exception exception) { rule.IsEnabled = old; _feedback.ShowProjectedNotification("保存启用状态失败", _feedback.Project(exception)); }
    }
    [RelayCommand] private async Task MoveUpAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken) => await MoveAsync(rule, -1, cancellationToken);
    [RelayCommand] private async Task MoveDownAsync(RegexReplacementRuleListItemViewModel? rule, CancellationToken cancellationToken) => await MoveAsync(rule, 1, cancellationToken);
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        Validate(); if (!CanSave) return;
        try { IsBusy = true; var prior = _baseline; var saved = await _workspace.SaveEditorAsync(new RegexReplacementRuleEditorModel(SelectedRuleId, DraftName, DraftPattern, DraftReplacement, DraftScope), cancellationToken); Open(saved, false); await RefreshAsync(saved.Id, false, cancellationToken); if (prior is null || prior.Pattern != saved.Pattern || prior.Replacement != saved.Replacement || prior.Scope != saved.Scope) await _playback.RefreshRegexReplacementAsync(cancellationToken); _feedback.ShowSuccess("正则替换规则已保存", saved.Name); }
        catch (Exception exception) { _feedback.ShowProjectedNotification("保存正则替换规则失败", _feedback.Project(exception)); }
        finally { IsBusy = false; }
    }
    [RelayCommand] private void Cancel() { if (_baseline is not null) Open(_baseline, false); else { HasEditor = false; IsEditingNewRule = false; } }
    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedRuleId is not Guid id || !CanDelete) return;
        var item = Rules.FirstOrDefault(rule => rule.Id == id); if (item is null || await _feedback.ConfirmDeletionAsync("删除正则替换规则", $"将删除规则“{item.Name}”。此操作不可撤销。", cancellationToken) != AppConfirmationDecision.Confirm) return;
        try { await _workspace.DeleteRuleAsync(id, cancellationToken); HasEditor = false; SelectedRuleId = null; await RefreshAsync(null, true, cancellationToken); await _playback.RefreshRegexReplacementAsync(cancellationToken); }
        catch (Exception exception) { _feedback.ShowProjectedNotification("删除正则替换规则失败", _feedback.Project(exception)); }
    }

    partial void OnDraftNameChanged(string value) => Changed();
    partial void OnDraftPatternChanged(string value) => Changed();
    partial void OnDraftReplacementChanged(string value) => Changed();
    partial void OnDraftScopeChanged(RegexReplacementScope value) => Changed();
    private void Changed() { if (!_loading) { Validate(); HasUnsavedChanges = _baseline is null || _baseline.Name != DraftName || _baseline.Pattern != DraftPattern || _baseline.Replacement != DraftReplacement || _baseline.Scope != DraftScope; OnPropertyChanged(nameof(CanSave)); } }
    private void Validate() { try { if (string.IsNullOrWhiteSpace(DraftName)) throw new ArgumentException("规则名称不能为空。"); if (string.IsNullOrWhiteSpace(DraftPattern)) throw new ArgumentException("正则表达式不能为空。"); _ = new Regex(DraftPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); ValidationMessage = string.Empty; } catch (ArgumentException exception) { ValidationMessage = exception.Message; } OnPropertyChanged(nameof(CanSave)); }
    private async Task MoveAsync(RegexReplacementRuleListItemViewModel? rule, int delta, CancellationToken ct) { if (rule is null) return; var ids = Rules.Select(item => item.Id).ToList(); var index = ids.IndexOf(rule.Id); var target = index + delta; if (target < 0 || target >= ids.Count) return; (ids[index], ids[target]) = (ids[target], ids[index]); try { await _workspace.SaveOrderAsync(ids, ct); await RefreshAsync(SelectedRuleId, false, ct); await _playback.RefreshRegexReplacementAsync(ct); } catch (Exception ex) { _feedback.ShowProjectedNotification("保存排序失败", _feedback.Project(ex)); } }
    private async Task LoadEditorAsync(Guid id, CancellationToken ct) { var editor = await _workspace.GetEditorAsync(id, ct); if (editor is not null) Open(editor, false); }
    private void Open(RegexReplacementRuleEditorModel editor, bool isNew) { _loading = true; _baseline = isNew ? null : editor; SelectedRuleId = editor.Id; DraftName = editor.Name; DraftPattern = editor.Pattern; DraftReplacement = editor.Replacement; DraftScope = editor.Scope; IsEditingNewRule = isNew; HasEditor = true; HasUnsavedChanges = false; _loading = false; Validate(); foreach (var rule in Rules) rule.IsSelected = rule.Id == editor.Id; }
    private async Task RefreshAsync(Guid? preferred, bool selectFirst, CancellationToken ct) { var items = await _workspace.GetRulesAsync(ct); Rules.Clear(); foreach (var item in items) Rules.Add(new RegexReplacementRuleListItemViewModel(item.Id, item.Name, item.PatternSummary, item.IsEnabled, item.Scope, item.Id == SelectedRuleId, item.ErrorMessage)); if (selectFirst && !IsEditingNewRule && items.FirstOrDefault() is { } first) await LoadEditorAsync(preferred is Guid id && items.Any(item => item.Id == id) ? id : first.Id, ct); }
}
