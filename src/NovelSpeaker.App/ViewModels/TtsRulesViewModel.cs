using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Pages;
using NovelSpeaker.Domain.Speech;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Drives the TTS rules workspace, including list selection, draft editing, import, and audition flows.
/// </summary>
public sealed partial class TtsRulesViewModel : ObservableObject
{
    private const string FixedTestText = "你好，欢迎试听。";

    private readonly ITtsRuleLibraryService _ruleLibraryService;
    private readonly ITtsRuleTestService _ruleTestService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly IAppSettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private CancellationTokenSource? _testOperationCts;
    private TtsRuleEditorModel? _baselineEditor;
    private long? _fallbackRuleId;
    private int _defaultSpeakSpeed = 10;

    public TtsRulesViewModel(
        ITtsRuleLibraryService ruleLibraryService,
        ITtsRuleTestService ruleTestService,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        IAppSettingsService settingsService,
        INavigationService navigationService)
    {
        _ruleLibraryService = ruleLibraryService;
        _ruleTestService = ruleTestService;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _navigationService = navigationService;
    }

    public ObservableCollection<TtsRuleListItemViewModel> Rules { get; } = [];

    public ObservableCollection<EditableKeyValueItemViewModel> HeaderEntries { get; } = [];

    [ObservableProperty]
    private bool hasEditor;

    [ObservableProperty]
    private bool isEditingNewRule;

    [ObservableProperty]
    private bool hasUnsavedChanges;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isTestBusy;

    [ObservableProperty]
    private bool isHelpDrawerOpen;

    [ObservableProperty]
    private long? highlightedRuleId;

    [ObservableProperty]
    private string draftName = string.Empty;

    [ObservableProperty]
    private bool draftIsEnabled = true;

    [ObservableProperty]
    private string draftUrl = string.Empty;

    [ObservableProperty]
    private string draftRequestMethod = "GET";

    [ObservableProperty]
    private string draftContentType = string.Empty;

    [ObservableProperty]
    private string draftRequestBody = string.Empty;

    [ObservableProperty]
    private string draftConcurrentRate = string.Empty;

    public bool CanSaveDraft => HasEditor && !IsBusy;

    public bool CanCancelEditing => HasEditor && !IsBusy;

    public bool CanDeleteCurrentRule => HasEditor && !IsEditingNewRule && CurrentRuleId is not null && !IsBusy;

    public bool CanSetCurrentRule => HasEditor && !IsEditingNewRule && CurrentRuleId is not null && !IsBusy;

    public bool CanExportDraft => HasEditor && !IsBusy;

    public bool CanTestDraft => HasEditor && !IsBusy;

    public bool IsPostMethod => string.Equals(DraftRequestMethod, "POST", StringComparison.OrdinalIgnoreCase);

    public long? CurrentRuleId => IsEditingNewRule ? null : _baselineEditor?.Id;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
        _defaultSpeakSpeed = settings.DefaultSpeakSpeed;

        await RefreshRulesAsync(
            preferredRuleId: HighlightedRuleId ?? Rules.FirstOrDefault(rule => rule.IsCurrent)?.Id,
            openEditorIfNeeded: !HasEditor,
            cancellationToken);
    }

    public void HandleNavigatedFrom()
    {
        CancelCurrentTest();
        IsHelpDrawerOpen = false;
    }

    public async Task ImportFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveDraftAsync(cancellationToken))
        {
            return;
        }

        try
        {
            var jsonText = await File.ReadAllTextAsync(filePath, cancellationToken);
            await ImportJsonTextAsyncCore(jsonText, Path.GetFileName(filePath), cancellationToken);
        }
        catch (Exception exception)
        {
            HandleProjectedError("规则导入失败", exception);
        }
    }

    public async Task ImportJsonTextAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveDraftAsync(cancellationToken))
        {
            return;
        }

        await ImportJsonTextAsyncCore(jsonText, sourceDescription, cancellationToken);
    }

    public async Task ExportDraftToFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!HasEditor)
        {
            _feedbackService.ShowWarning("导出失败", "请先打开一条规则或新建规则。");
            return;
        }

        var draft = BuildCurrentEditorModel();
        var validation = await _ruleLibraryService.ValidateEditorAsync(draft, cancellationToken);
        if (!validation.IsValid)
        {
            _feedbackService.ShowWarning("导出失败", string.Join(" ", validation.Errors));
            return;
        }

        var json = await _ruleLibraryService.ExportEditorJsonAsync(draft, cancellationToken);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        _feedbackService.ShowSuccess("规则已导出", $"已导出规则：{validation.NormalizedModel.Name}。");
    }

    public void NotifyClipboardTextMissing()
    {
        _feedbackService.ShowWarning("无法导入", "剪贴板中没有可导入的文本内容。");
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveDraftAsync(cancellationToken))
        {
            return;
        }

        CancelCurrentTest();
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

        CancelCurrentTest();
        OpenEditor(CreateEmptyEditor(), true, HighlightedRuleId);
    }

    [RelayCommand]
    private async Task SelectRuleAsync(TtsRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null)
        {
            return;
        }

        if (HighlightedRuleId == rule.Id && !IsEditingNewRule)
        {
            return;
        }

        if (!await ConfirmLeaveDraftAsync(cancellationToken))
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
            if (_fallbackRuleId is long fallbackRuleId)
            {
                await OpenSavedRuleAsync(fallbackRuleId, cancellationToken);
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
    private async Task SetCurrentRuleAsync(CancellationToken cancellationToken)
    {
        if (!HasEditor)
        {
            return;
        }

        long ruleId;
        string ruleName;
        var isEnabled = DraftIsEnabled;
        if (HasUnsavedChanges || IsEditingNewRule)
        {
            var savedRule = await SaveDraftCoreAsync(cancellationToken);
            if (savedRule is null)
            {
                return;
            }

            ruleId = savedRule.Id;
            ruleName = savedRule.Name;
            isEnabled = savedRule.IsEnabled;
        }
        else if (CurrentRuleId is long currentRuleId)
        {
            ruleId = currentRuleId;
            ruleName = DraftName;
        }
        else
        {
            return;
        }

        if (!isEnabled)
        {
            _feedbackService.ShowWarning("无法设为当前", "请先启用规则，再将其设为当前规则。");
            return;
        }

        try
        {
            await _ruleLibraryService.SelectRuleAsync(ruleId, cancellationToken);
            await RefreshRulesAsync(ruleId, openEditorIfNeeded: false, cancellationToken);
            _feedbackService.ShowSuccess("当前规则已更新", $"当前规则已切换为：{ruleName}。");
        }
        catch (Exception exception)
        {
            HandleProjectedError("规则切换失败", exception);
        }
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(CancellationToken cancellationToken)
    {
        if (!CanDeleteCurrentRule || CurrentRuleId is not long ruleId)
        {
            return;
        }

        if (HasUnsavedChanges)
        {
            var decision = await _dialogService.ShowUnsavedChangesAsync(
                "未保存的修改",
                "当前规则有未保存的修改。要先保存再删除吗？",
                "保存",
                "放弃",
                "取消",
                cancellationToken);

            if (decision == UnsavedChangesDecision.Cancel)
            {
                return;
            }

            if (decision == UnsavedChangesDecision.Save)
            {
                var savedRule = await EnsureSavedDraftAsync(cancellationToken);
                if (savedRule is null)
                {
                    return;
                }

                ruleId = savedRule.Id;
            }
        }

        var currentRule = Rules.FirstOrDefault(rule => rule.Id == ruleId);
        if (currentRule is null)
        {
            return;
        }

        var isCurrentRule = currentRule.IsCurrent;
        var confirmed = isCurrentRule
            ? await _dialogService.ShowConfirmationAsync(
                "删除当前规则",
                $"删除“{currentRule.Name}”后，应用将进入无当前规则状态。确定继续吗？",
                "继续",
                "取消",
                cancellationToken)
            : await _feedbackService.ConfirmDeletionAsync(
                "删除规则",
                $"将删除规则“{currentRule.Name}”。此操作不可撤销。",
                cancellationToken);
        if (confirmed != AppConfirmationDecision.Confirm)
        {
            return;
        }

        try
        {
            CancelCurrentTest();
            if (isCurrentRule)
            {
                await _ruleLibraryService.ApplyRuleMutationAsync(
                    new TtsRuleMutationDecision(ruleId, TtsRuleMutationAction.Delete, null, true),
                    cancellationToken);
            }
            else
            {
                await _ruleLibraryService.DeleteRuleAsync(ruleId, cancellationToken);
            }

            CloseEditor();
            await RefreshRulesAsync(null, openEditorIfNeeded: true, cancellationToken);
            _feedbackService.ShowSuccess("规则已删除", $"已删除规则：{currentRule.Name}。");
        }
        catch (Exception exception)
        {
            HandleProjectedError("规则删除失败", exception);
        }
    }

    [RelayCommand]
    private async Task TestDraftAsync(CancellationToken cancellationToken)
    {
        if (!HasEditor)
        {
            return;
        }

        using var linkedCts = BeginTestOperation(cancellationToken);
        try
        {
            var result = await _ruleTestService.TestAsync(
                new TtsRuleDraftTestInput(BuildCurrentEditorModel(), FixedTestText, _defaultSpeakSpeed),
                linkedCts.Token);

            if (result.IsSuccess)
            {
                _feedbackService.ShowSuccess("试听已开始", result.Message);
            }
            else
            {
                _feedbackService.ShowWarning("试听失败", result.Message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            HandleProjectedError("试听失败", exception);
        }
        finally
        {
            EndTestOperation(linkedCts);
        }
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

    [RelayCommand]
    private void AddHeaderEntry()
    {
        AddEditableEntry(HeaderEntries);
    }

    [RelayCommand]
    private void RemoveHeaderEntry(EditableKeyValueItemViewModel? item)
    {
        RemoveEditableEntry(HeaderEntries, item);
    }

    partial void OnDraftNameChanged(string value) => NotifyDraftChanged();
    partial void OnDraftIsEnabledChanged(bool value) => NotifyDraftChanged();
    partial void OnDraftUrlChanged(string value) => NotifyDraftChanged();

    partial void OnDraftRequestMethodChanged(string value)
    {
        OnPropertyChanged(nameof(IsPostMethod));
        NotifyDraftChanged();
    }

    partial void OnDraftContentTypeChanged(string value) => NotifyDraftChanged();
    partial void OnDraftRequestBodyChanged(string value) => NotifyDraftChanged();
    partial void OnDraftConcurrentRateChanged(string value) => NotifyDraftChanged();

    private async Task ImportJsonTextAsyncCore(string jsonText, string sourceDescription, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _ruleLibraryService.ImportJsonTextAsync(jsonText, sourceDescription, cancellationToken);
            await RefreshRulesAsync(result.FirstImportedRuleId, openEditorIfNeeded: true, cancellationToken);
            _feedbackService.ShowSuccess("规则导入完成", BuildImportStatusMessage(result));
        }
        catch (Exception exception)
        {
            HandleProjectedError("规则导入失败", exception);
        }
    }

    private async Task RefreshRulesAsync(long? preferredRuleId, bool openEditorIfNeeded, CancellationToken cancellationToken)
    {
        var rules = await _ruleLibraryService.GetRulesAsync(cancellationToken);
        Rules.ReplaceWith(
            rules,
            rule => new TtsRuleListItemViewModel(
                rule.Id,
                rule.Name,
                rule.IsEnabled,
                rule.IsSelected,
                HighlightedRuleId == rule.Id && !IsEditingNewRule));

        OnPropertyChanged(nameof(CurrentRuleId));

        if (openEditorIfNeeded)
        {
            var targetRuleId = preferredRuleId;
            if (targetRuleId is null)
            {
                targetRuleId = rules.FirstOrDefault(rule => rule.IsSelected)?.Id ?? rules.FirstOrDefault()?.Id;
            }

            if (targetRuleId is long ruleId)
            {
                await OpenSavedRuleAsync(ruleId, cancellationToken);
            }
            else if (!IsEditingNewRule)
            {
                CloseEditor();
            }
        }

        NotifyUiStateChanged();
    }

    private async Task OpenSavedRuleAsync(long ruleId, CancellationToken cancellationToken)
    {
        var editor = await _ruleLibraryService.GetEditorAsync(ruleId, cancellationToken);
        if (editor is null)
        {
            CloseEditor();
            return;
        }

        CancelCurrentTest();
        OpenEditor(editor, false, ruleId);
    }

    private void OpenEditor(TtsRuleEditorModel editor, bool isNew, long? fallbackRuleId)
    {
        _baselineEditor = editor;
        _fallbackRuleId = fallbackRuleId;
        IsEditingNewRule = isNew;
        HighlightedRuleId = isNew ? null : editor.Id;
        HasEditor = true;
        UpdateRuleSelectionStates();

        SetDraftFields(editor);
        UpdateUnsavedChanges();
        NotifyUiStateChanged();
    }

    private void CloseEditor()
    {
        _baselineEditor = null;
        _fallbackRuleId = null;
        HighlightedRuleId = null;
        IsEditingNewRule = false;
        HasEditor = false;
        UpdateRuleSelectionStates();
        DraftName = string.Empty;
        DraftIsEnabled = true;
        DraftUrl = string.Empty;
        DraftRequestMethod = "GET";
        DraftContentType = string.Empty;
        DraftRequestBody = string.Empty;
        DraftConcurrentRate = string.Empty;
        ResetEditableCollection(HeaderEntries, []);
        UpdateUnsavedChanges();
        OnPropertyChanged(nameof(IsPostMethod));
        NotifyUiStateChanged();
    }

    private void SetDraftFields(TtsRuleEditorModel editor)
    {
        DraftName = editor.Name;
        DraftIsEnabled = editor.IsEnabled;
        DraftUrl = editor.Url;
        DraftRequestMethod = editor.RequestOptions.Method ?? "GET";
        DraftContentType = editor.ContentType ?? string.Empty;
        DraftRequestBody = editor.RequestOptions.Body ?? string.Empty;
        DraftConcurrentRate = editor.ConcurrentRate ?? string.Empty;
        ResetEditableCollection(HeaderEntries, editor.Headers);
        OnPropertyChanged(nameof(IsPostMethod));
    }

    private void ResetEditableCollection(
        ObservableCollection<EditableKeyValueItemViewModel> target,
        IReadOnlyList<TtsRuleEditorKeyValue> values)
    {
        foreach (var existing in target)
        {
            existing.PropertyChanged -= EditableEntryOnPropertyChanged;
        }

        target.Clear();
        foreach (var value in values)
        {
            var item = new EditableKeyValueItemViewModel(value.Key, value.Value);
            item.PropertyChanged += EditableEntryOnPropertyChanged;
            target.Add(item);
        }
    }

    private void AddEditableEntry(ObservableCollection<EditableKeyValueItemViewModel> target)
    {
        if (!HasEditor)
        {
            return;
        }

        var item = new EditableKeyValueItemViewModel();
        item.PropertyChanged += EditableEntryOnPropertyChanged;
        target.Add(item);
        UpdateUnsavedChanges();
    }

    private void RemoveEditableEntry(
        ObservableCollection<EditableKeyValueItemViewModel> target,
        EditableKeyValueItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.PropertyChanged -= EditableEntryOnPropertyChanged;
        target.Remove(item);
        UpdateUnsavedChanges();
    }

    private void EditableEntryOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateUnsavedChanges();
    }

    private void NotifyDraftChanged()
    {
        UpdateUnsavedChanges();
    }

    private void UpdateUnsavedChanges()
    {
        HasUnsavedChanges = HasEditor &&
                            _baselineEditor is not null &&
                            !EditorsEqual(_baselineEditor, BuildCurrentEditorModel());
        NotifyUiStateChanged();
    }

    private void NotifyUiStateChanged()
    {
        OnPropertyChanged(nameof(CanSaveDraft));
        OnPropertyChanged(nameof(CanCancelEditing));
        OnPropertyChanged(nameof(CanDeleteCurrentRule));
        OnPropertyChanged(nameof(CanSetCurrentRule));
        OnPropertyChanged(nameof(CanExportDraft));
        OnPropertyChanged(nameof(CanTestDraft));
    }

    private void UpdateRuleSelectionStates()
    {
        foreach (var rule in Rules)
        {
            rule.IsSelected = !IsEditingNewRule && HighlightedRuleId == rule.Id;
        }
    }

    private async Task<bool> ConfirmLeaveDraftAsync(CancellationToken cancellationToken)
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var decision = await _dialogService.ShowUnsavedChangesAsync(
            "未保存的修改",
            "当前规则有未保存的修改。要先保存再继续吗？",
            "保存",
            "放弃",
            "取消",
            cancellationToken);

        return decision switch
        {
            UnsavedChangesDecision.Save => await SaveDraftCoreAsync(cancellationToken) is not null,
            UnsavedChangesDecision.Discard => DiscardCurrentDraftAndCloseIfNeeded(),
            _ => false
        };
    }

    private async Task<HttpTtsRule?> EnsureSavedDraftAsync(CancellationToken cancellationToken)
    {
        return await SaveDraftCoreAsync(cancellationToken);
    }

    private async Task<HttpTtsRule?> SaveDraftCoreAsync(CancellationToken cancellationToken)
    {
        if (!HasEditor)
        {
            return null;
        }

        IsBusy = true;
        NotifyUiStateChanged();

        try
        {
            var draft = BuildCurrentEditorModel();
            var currentRule = CurrentRuleId is long currentRuleId
                ? Rules.FirstOrDefault(rule => rule.Id == currentRuleId)
                : null;
            if (currentRule?.IsCurrent == true && !draft.IsEnabled)
            {
                var confirmation = await _dialogService.ShowConfirmationAsync(
                    "禁用当前规则",
                    $"禁用“{draft.Name}”后，应用将进入无当前规则状态。确定继续吗？",
                    "继续",
                    "取消",
                    cancellationToken);
                if (confirmation != AppConfirmationDecision.Confirm)
                {
                    return null;
                }
            }

            var savedRule = await _ruleLibraryService.SaveEditorAsync(draft, cancellationToken);
            if (currentRule?.IsCurrent == true && !savedRule.IsEnabled)
            {
                await _ruleLibraryService.ApplyRuleMutationAsync(
                    new TtsRuleMutationDecision(savedRule.Id, TtsRuleMutationAction.Disable, null, true),
                    cancellationToken);
            }

            await RefreshRulesAsync(savedRule.Id, openEditorIfNeeded: false, cancellationToken);
            var savedEditor = await _ruleLibraryService.GetEditorAsync(savedRule.Id, cancellationToken);
            if (savedEditor is not null)
            {
                OpenEditor(savedEditor, false, savedRule.Id);
            }

            _feedbackService.ShowSuccess("规则已保存", $"规则已保存：{savedRule.Name}。");
            return savedRule;
        }
        catch (Exception exception)
        {
            HandleProjectedError("规则保存失败", exception);
            return null;
        }
        finally
        {
            IsBusy = false;
            NotifyUiStateChanged();
        }
    }

    private TtsRuleEditorModel BuildCurrentEditorModel()
    {
        return new TtsRuleEditorModel(
            IsEditingNewRule ? null : _baselineEditor?.Id,
            DraftName,
            DraftIsEnabled,
            DraftUrl,
            NullIfWhitespace(DraftContentType),
            NullIfWhitespace(DraftConcurrentRate),
            _baselineEditor?.LastUpdateTime,
            ToEditorEntries(HeaderEntries),
            new TtsRuleRequestOptionsEditor(
                NullIfWhitespace(DraftRequestMethod)?.ToUpperInvariant(),
                IsPostMethod ? NullIfWhitespace(DraftRequestBody) : null));
    }

    private static IReadOnlyList<TtsRuleEditorKeyValue> ToEditorEntries(
        ObservableCollection<EditableKeyValueItemViewModel> items)
    {
        return items
            .Select(item => new TtsRuleEditorKeyValue(item.Key, item.Value))
            .ToArray();
    }

    private static bool EditorsEqual(TtsRuleEditorModel left, TtsRuleEditorModel right)
    {
        return left.Id == right.Id &&
               left.Name == right.Name &&
               left.IsEnabled == right.IsEnabled &&
               left.Url == right.Url &&
               left.ContentType == right.ContentType &&
               left.ConcurrentRate == right.ConcurrentRate &&
               left.RequestOptions.Method == right.RequestOptions.Method &&
               left.RequestOptions.Body == right.RequestOptions.Body &&
               EntryListsEqual(left.Headers, right.Headers);
    }

    private static bool EntryListsEqual(
        IReadOnlyList<TtsRuleEditorKeyValue> left,
        IReadOnlyList<TtsRuleEditorKeyValue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].Key != right[index].Key || left[index].Value != right[index].Value)
            {
                return false;
            }
        }

        return true;
    }

    private static string? NullIfWhitespace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static TtsRuleEditorModel CreateEmptyEditor()
    {
        return new TtsRuleEditorModel(
            null,
            string.Empty,
            true,
            string.Empty,
            null,
            null,
            null,
            [],
            new TtsRuleRequestOptionsEditor("GET", null));
    }

    private CancellationTokenSource BeginTestOperation(CancellationToken cancellationToken)
    {
        _testOperationCts?.Cancel();
        _testOperationCts?.Dispose();
        _testOperationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsTestBusy = true;
        NotifyUiStateChanged();
        return _testOperationCts;
    }

    private void EndTestOperation(CancellationTokenSource operationCts)
    {
        if (ReferenceEquals(_testOperationCts, operationCts))
        {
            _testOperationCts = null;
        }

        IsTestBusy = false;
        NotifyUiStateChanged();
    }

    private void CancelCurrentTest()
    {
        _testOperationCts?.Cancel();
    }

    private void DiscardCurrentDraft()
    {
        if (!HasEditor)
        {
            return;
        }

        if (IsEditingNewRule)
        {
            CloseEditor();
            return;
        }

        if (_baselineEditor is not null)
        {
            OpenEditor(_baselineEditor, false, _fallbackRuleId);
        }
    }

    private bool DiscardCurrentDraftAndCloseIfNeeded()
    {
        DiscardCurrentDraft();
        return true;
    }

    private static string BuildImportStatusMessage(TtsRuleImportResult result)
    {
        return $"新增 {result.ImportedCount} 条，失败 {result.FailedCount} 条，跳过 {result.SkippedCount} 条。";
    }

    private void HandleProjectedError(string title, Exception exception)
    {
        var projected = _feedbackService.Project(exception);
        _feedbackService.ShowProjectedNotification(title, projected);
    }
}
