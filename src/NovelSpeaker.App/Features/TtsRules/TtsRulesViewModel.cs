using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Features.RuleEditing;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.App.Features.TtsRules;

/// <summary>
/// Drives the TTS rules workspace, including list selection, draft editing, import, and audition flows.
/// </summary>
public sealed partial class TtsRulesViewModel : ObservableObject
{
    private const string FixedTestText = "你好，欢迎试听。";

    private readonly ITtsRuleImportUseCase _ruleImport;
    private readonly ITtsRuleEditorUseCase _ruleEditor;
    private readonly ITtsRuleSelectionUseCase _ruleSelection;
    private readonly ITtsRuleQueries _ruleQueries;
    private readonly ITtsRuleTestService _ruleTestService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly IAppSettingsService _settingsService;
    private readonly IAppNavigator _navigator;
    private readonly IUserDocumentFileOperations _fileOperations;
    private CancellationTokenSource? _testOperationCts;
    private readonly EditorSession<long?, TtsRuleEditorModel> _editorSession = new(EditorsEqual);
    private int _defaultSpeakSpeed = 10;

    public TtsRulesViewModel(
        ITtsRuleImportUseCase ruleImport,
        ITtsRuleEditorUseCase ruleEditor,
        ITtsRuleSelectionUseCase ruleSelection,
        ITtsRuleQueries ruleQueries,
        ITtsRuleTestService ruleTestService,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        IAppSettingsService settingsService,
        IAppNavigator navigator,
        IUserDocumentFileOperations fileOperations)
    {
        _ruleImport = ruleImport;
        _ruleEditor = ruleEditor;
        _ruleSelection = ruleSelection;
        _ruleQueries = ruleQueries;
        _ruleTestService = ruleTestService;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _navigator = navigator;
        _fileOperations = fileOperations;
    }

    public ObservableCollection<TtsRuleListItemViewModel> Rules { get; } = [];

    public ObservableCollection<EditableKeyValueItemViewModel> HeaderEntries { get; } = [];

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

    public bool HasEditor => _editorSession.HasEditor;

    public bool IsEditingNewRule => _editorSession.IsNew;

    public bool HasUnsavedChanges => _editorSession.IsDirty;

    public bool CanSaveDraft => HasEditor && HasUnsavedChanges && !IsBusy;

    public bool CanCancelEditing => HasEditor && HasUnsavedChanges && !IsBusy;

    public bool CanTestDraft => HasEditor && !IsBusy;

    public bool IsPostMethod => string.Equals(DraftRequestMethod, "POST", StringComparison.OrdinalIgnoreCase);

    public long? CurrentRuleId => IsEditingNewRule ? null : _editorSession.EditorId;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = _settingsService.Current;
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
        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        try
        {
            var metadata = await _fileOperations.GetMetadataAsync(filePath, cancellationToken);
            var jsonText = await _fileOperations.ReadTextAsync(filePath, cancellationToken);
            await ImportJsonTextAsyncCore(
                jsonText,
                metadata?.FileName ?? "所选规则文件",
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleProjectedError("规则导入失败", exception);
        }
    }

    public async Task ImportJsonTextAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        await ImportJsonTextAsyncCore(jsonText, sourceDescription, cancellationToken);
    }

    public async Task ExportRuleToFileAsync(
        TtsRuleListItemViewModel rule,
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var json = await _ruleQueries.ExportRuleJsonAsync(rule.Id, cancellationToken);
        if (json is null)
        {
            _feedbackService.ShowWarning("导出失败", "未找到要导出的规则，请刷新后重试。");
            return;
        }

        await _fileOperations.WriteTextAsync(filePath, json, cancellationToken);
        _feedbackService.ShowSuccess("规则已导出", $"已导出规则：{rule.Name}。");
    }

    public void NotifyClipboardTextMissing()
    {
        _feedbackService.ShowWarning("无法导入", "剪贴板中没有可导入的文本内容。");
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        CancelCurrentTest();
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

        if (!await ConfirmLeaveAsync(cancellationToken))
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
            if (_editorSession.FallbackId is long fallbackRuleId)
            {
                await OpenSavedRuleAsync(fallbackRuleId, cancellationToken);
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
    private async Task SetCurrentRuleAsync(
        TtsRuleListItemViewModel? rule,
        CancellationToken cancellationToken)
    {
        if (rule is null || rule.IsCurrent || !rule.IsEnabled || IsBusy)
        {
            return;
        }

        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        try
        {
            await _ruleSelection.SelectRuleAsync(rule.Id, cancellationToken);
            await RefreshRulesAsync(
                HighlightedRuleId,
                openEditorIfNeeded: HasEditor,
                cancellationToken);
            _feedbackService.ShowSuccess("当前规则已更新", $"当前规则已切换为：{rule.Name}。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleProjectedError("规则切换失败", exception);
        }
    }

    [RelayCommand]
    private async Task ToggleRuleEnabledAsync(
        TtsRuleListItemViewModel? rule,
        CancellationToken cancellationToken)
    {
        if (rule is null || IsBusy)
        {
            return;
        }

        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        try
        {
            if (rule.IsEnabled)
            {
                if (rule.IsCurrent)
                {
                    var confirmation = await _dialogService.ShowConfirmationAsync(
                        "禁用当前规则",
                        $"禁用“{rule.Name}”后，应用将进入无当前规则状态。确定继续吗？",
                        "继续",
                        "取消",
                        cancellationToken);
                    if (confirmation != AppConfirmationDecision.Confirm)
                    {
                        return;
                    }
                }

                await _ruleSelection.ApplyRuleMutationAsync(
                    new TtsRuleMutationDecision(
                        rule.Id,
                        TtsRuleMutationAction.Disable,
                        null,
                        rule.IsCurrent),
                    cancellationToken);
            }
            else
            {
                var editor = await _ruleEditor.GetEditorAsync(rule.Id, cancellationToken);
                if (editor is null)
                {
                    _feedbackService.ShowWarning("启用失败", "未找到要启用的规则，请刷新后重试。");
                    return;
                }

                await _ruleEditor.SaveEditorAsync(editor with { IsEnabled = true }, cancellationToken);
            }

            await RefreshRulesAsync(
                HighlightedRuleId,
                openEditorIfNeeded: HasEditor,
                cancellationToken);
            _feedbackService.ShowSuccess(
                rule.IsEnabled ? "规则已禁用" : "规则已启用",
                $"{(rule.IsEnabled ? "已禁用" : "已启用")}规则：{rule.Name}。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleProjectedError(rule.IsEnabled ? "规则禁用失败" : "规则启用失败", exception);
        }
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(
        TtsRuleListItemViewModel? rule,
        CancellationToken cancellationToken)
    {
        if (rule is not null)
        {
            await DeleteRuleFromListAsync(rule, cancellationToken);
        }
    }

    public async Task DeleteRuleFromListAsync(
        TtsRuleListItemViewModel rule,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (IsBusy)
        {
            return;
        }

        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        var confirmed = rule.IsCurrent
            ? await _dialogService.ShowConfirmationAsync(
                "删除当前规则",
                $"删除“{rule.Name}”后，应用将进入无当前规则状态。确定继续吗？",
                "继续",
                "取消",
                cancellationToken)
            : await _feedbackService.ConfirmDeletionAsync(
                "删除规则",
                $"将删除规则“{rule.Name}”。此操作不可撤销。",
                cancellationToken);
        if (confirmed != AppConfirmationDecision.Confirm)
        {
            return;
        }

        try
        {
            CancelCurrentTest();
            await _ruleSelection.ApplyRuleMutationAsync(
                new TtsRuleMutationDecision(
                    rule.Id,
                    TtsRuleMutationAction.Delete,
                    null,
                    rule.IsCurrent),
                cancellationToken);

            var deletedOpenEditor = CurrentRuleId == rule.Id;
            if (deletedOpenEditor)
            {
                CloseEditor();
            }

            await RefreshRulesAsync(
                deletedOpenEditor ? null : HighlightedRuleId,
                openEditorIfNeeded: true,
                cancellationToken);
            _feedbackService.ShowSuccess("规则已删除", $"已删除规则：{rule.Name}。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
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
            var preview = await _ruleImport.CreateImportPreviewAsync(
                jsonText,
                sourceDescription,
                cancellationToken);
            if (preview.ErrorMessage is not null)
            {
                _feedbackService.ShowWarning("无法导入", preview.ErrorMessage);
                return;
            }

            var hasCookieLoginInfoDependency = preview.Items.Any(item =>
                !item.CanImport &&
                item.StatusMessage.Contains("Cookie/LoginInfo", StringComparison.OrdinalIgnoreCase));
            var result = await _ruleImport.ImportAsync(preview, cancellationToken);
            await RefreshRulesAsync(result.FirstImportedRuleId, openEditorIfNeeded: true, cancellationToken);
            var statusMessage = BuildImportStatusMessage(result);
            if (hasCookieLoginInfoDependency)
            {
                _feedbackService.ShowWarning(
                    "部分规则不兼容",
                    $"当前版本不支持 Cookie/LoginInfo。{statusMessage}");
            }
            else
            {
                _feedbackService.ShowSuccess("规则导入完成", statusMessage);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleProjectedError("规则导入失败", exception);
        }
    }

    private async Task RefreshRulesAsync(long? preferredRuleId, bool openEditorIfNeeded, CancellationToken cancellationToken)
    {
        var rules = await _ruleQueries.GetRulesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Rules.ReplaceWith(
            rules,
            rule => new TtsRuleListItemViewModel(
                rule.Id,
                rule.Name,
                rule.RequestSummary,
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
        var editor = await _ruleEditor.GetEditorAsync(ruleId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
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
        _editorSession.Open(isNew ? null : editor.Id, editor, isNew, fallbackRuleId);
        HighlightedRuleId = isNew ? null : editor.Id;
        UpdateRuleSelectionStates();

        SetDraftFields(editor);
        UpdateUnsavedChanges();
        NotifyUiStateChanged();
    }

    private void CloseEditor()
    {
        _editorSession.Close();
        HighlightedRuleId = null;
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
        _editorSession.UpdateDirty(BuildCurrentEditorModel());
        NotifyUiStateChanged();
    }

    private void NotifyUiStateChanged()
    {
        OnPropertyChanged(nameof(HasEditor));
        OnPropertyChanged(nameof(IsEditingNewRule));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanSaveDraft));
        OnPropertyChanged(nameof(CanCancelEditing));
        OnPropertyChanged(nameof(CanTestDraft));
    }

    private void UpdateRuleSelectionStates()
    {
        foreach (var rule in Rules)
        {
            rule.IsSelected = !IsEditingNewRule && HighlightedRuleId == rule.Id;
        }
    }

    public async Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken)
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
        cancellationToken.ThrowIfCancellationRequested();

        return decision switch
        {
            UnsavedChangesDecision.Save => await SaveDraftCoreAsync(cancellationToken) is not null,
            UnsavedChangesDecision.Discard => DiscardCurrentDraftAndCloseIfNeeded(),
            _ => false
        };
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
            var validation = await _ruleEditor.ValidateEditorAsync(draft, cancellationToken);
            if (!validation.IsValid)
            {
                var validationMessage = string.Join(" ", validation.Errors);
                var title = validation.Errors.Any(error =>
                    error.Contains("Cookie/LoginInfo", StringComparison.OrdinalIgnoreCase))
                    ? "规则不兼容"
                    : "无法保存规则";
                _feedbackService.ShowWarning(title, validationMessage);
                return null;
            }

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

            var savedRule = await _ruleEditor.SaveEditorAsync(draft, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (currentRule?.IsCurrent == true && !savedRule.IsEnabled)
            {
                await _ruleSelection.ApplyRuleMutationAsync(
                    new TtsRuleMutationDecision(savedRule.Id, TtsRuleMutationAction.Disable, null, true),
                    cancellationToken);
            }

            await RefreshRulesAsync(savedRule.Id, openEditorIfNeeded: false, cancellationToken);
            var savedEditor = await _ruleEditor.GetEditorAsync(savedRule.Id, cancellationToken);
            if (savedEditor is not null)
            {
                OpenEditor(savedEditor, false, savedRule.Id);
            }

            _feedbackService.ShowSuccess("规则已保存", $"规则已保存：{savedRule.Name}。");
            return savedRule;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
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
            IsEditingNewRule ? null : _editorSession.EditorId,
            DraftName,
            DraftIsEnabled,
            DraftUrl,
            NullIfWhitespace(DraftContentType),
            NullIfWhitespace(DraftConcurrentRate),
            _editorSession.Baseline?.LastUpdateTime,
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

        if (_editorSession.Baseline is not null)
        {
            OpenEditor(_editorSession.Baseline, false, _editorSession.FallbackId);
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
