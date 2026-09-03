using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Features.RuleEditing;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.App.Features.TtsRules;

/// <summary>
/// Drives the TTS rules workspace, including list selection, draft editing, import, and audition flows.
/// </summary>
public sealed partial class TtsRulesViewModel : ObservableObject, ITransientEscapeHandler
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
    private readonly IRuleDocumentInteraction _ruleDocuments;
    private int _importOperationActive;
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
        IRuleDocumentInteraction ruleDocuments)
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
        _ruleDocuments = ruleDocuments;
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

    public bool CanCancelEditing => HasEditor && !IsBusy;

    public bool CanTestDraft => HasEditor && !IsBusy;

    public bool IsPostMethod => string.Equals(DraftRequestMethod, "POST", StringComparison.OrdinalIgnoreCase);

    public long? CurrentRuleId => IsEditingNewRule ? null : _editorSession.EditorId;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = _settingsService.Current;
        _defaultSpeakSpeed = settings.DefaultSpeakSpeed;

        await RefreshRulesAsync(HighlightedRuleId, openEditorIfNeeded: false, cancellationToken);
    }

    public void HandleNavigatedFrom()
    {
        CancelCurrentTest();
        IsHelpDrawerOpen = false;
    }

    public bool TryHandleEscape()
    {
        if (!IsHelpDrawerOpen)
        {
            return false;
        }

        IsHelpDrawerOpen = false;
        return true;
    }

    public Task ImportRuleFileAsync(CancellationToken cancellationToken) =>
        ImportDocumentAsync(
            () => _ruleDocuments.PickImportAsync(cancellationToken),
            "规则导入失败",
            cancellationToken);

    public Task ImportRulesFromClipboardAsync(CancellationToken cancellationToken) =>
        ImportDocumentAsync(
            () => _ruleDocuments.ReadClipboardAsync(cancellationToken),
            "从剪贴板导入失败",
            cancellationToken,
            warnWhenMissing: true);

    [RelayCommand]
    public async Task ExportRuleAsync(TtsRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null)
        {
            return;
        }

        try
        {
            var json = await _ruleQueries.ExportRuleJsonAsync(rule.Id, cancellationToken);
            if (json is null)
            {
                _feedbackService.ShowWarning("导出失败", "未找到要导出的规则，请刷新后重试。");
                return;
            }

            if (await _ruleDocuments.ExportAsync("tts-rule.json", json, cancellationToken))
            {
                _feedbackService.ShowSuccess("规则已导出", $"已导出规则：{rule.Name}。");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleProjectedError("规则导出失败", exception);
        }
    }

    [RelayCommand]
    public async Task CopyRuleAsync(TtsRuleListItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null)
        {
            return;
        }

        try
        {
            var json = await _ruleQueries.ExportRuleJsonAsync(rule.Id, cancellationToken);
            if (json is null)
            {
                _feedbackService.ShowWarning("复制失败", "未找到要复制的规则，请刷新后重试。");
                return;
            }

            await _ruleDocuments.CopyAsync(json, cancellationToken);
            _feedbackService.ShowSuccess("规则已复制", $"已复制规则：{rule.Name}。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleProjectedError("规则复制失败", exception);
        }
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken))
        {
            return;
        }

        CancelCurrentTest();
        await _navigator.NavigateBackAsync(cancellationToken, bypassGuard: true).ConfigureAwait(true);
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
    private Task CancelEditingAsync(CancellationToken cancellationToken)
    {
        if (!HasEditor)
        {
            return Task.CompletedTask;
        }

        CloseEditor();
        return Task.CompletedTask;
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

        var originalValue = rule.IsEnabled;
        rule.IsEnabled = !originalValue;
        var persisted = false;
        IsBusy = true;
        NotifyUiStateChanged();
        try
        {
            if (originalValue)
            {
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
                await _ruleEditor.SetRuleEnabledAsync(rule.Id, true, cancellationToken);
            }

            persisted = true;
            await RefreshRulesAsync(
                HighlightedRuleId,
                openEditorIfNeeded: false,
                cancellationToken);
            _feedbackService.ShowSuccess(
                originalValue ? "规则已禁用" : "规则已启用",
                $"{(originalValue ? "已禁用" : "已启用")}规则：{rule.Name}。");
        }
        catch (OperationCanceledException)
        {
            if (!persisted)
            {
                rule.IsEnabled = originalValue;
            }

            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!persisted)
            {
                rule.IsEnabled = originalValue;
            }

            HandleProjectedError(originalValue ? "规则禁用失败" : "规则启用失败", exception);
        }
        finally
        {
            IsBusy = false;
            NotifyUiStateChanged();
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

        var confirmed = await _feedbackService.ConfirmDeletionAsync(
            "删除规则",
            $"将删除规则“{rule.Name}”。此操作不可撤销。",
            cancellationToken);
        if (confirmed != AppConfirmationDecision.Confirm)
        {
            return;
        }

        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            NotifyUiStateChanged();
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
                openEditorIfNeeded: false,
                cancellationToken);
            _feedbackService.ShowSuccess("规则已删除", $"已删除规则：{rule.Name}。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleProjectedError("规则删除失败", exception);
        }
        finally
        {
            IsBusy = false;
            NotifyUiStateChanged();
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
    partial void OnDraftUrlChanged(string value) => NotifyDraftChanged();

    partial void OnDraftRequestMethodChanged(string value)
    {
        OnPropertyChanged(nameof(IsPostMethod));
        NotifyDraftChanged();
    }

    partial void OnDraftContentTypeChanged(string value) => NotifyDraftChanged();
    partial void OnDraftRequestBodyChanged(string value) => NotifyDraftChanged();
    partial void OnDraftConcurrentRateChanged(string value) => NotifyDraftChanged();

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
                    _feedbackService.ShowWarning("无法导入", "剪贴板中没有可导入的文本内容。");
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
            NotifyUiStateChanged();
            await ImportJsonTextAsyncCore(document.Json, document.SourceDescription, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleProjectedError(failureTitle, exception);
        }
        finally
        {
            if (ownsBusy)
            {
                IsBusy = false;
                NotifyUiStateChanged();
            }

            Volatile.Write(ref _importOperationActive, 0);
        }
    }

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
            await RefreshRulesAsync(null, openEditorIfNeeded: false, cancellationToken);
            var statusMessage = BuildImportStatusMessage(result);
            if (hasCookieLoginInfoDependency)
            {
                _feedbackService.ShowWarning(
                    "部分规则不兼容",
                    $"当前版本不支持 Cookie/LoginInfo。{statusMessage}");
            }
            else if (result.FailedCount > 0)
            {
                _feedbackService.ShowWarning("部分规则导入失败", statusMessage);
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
        if (!HasEditor || IsBusy)
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

            var savedRule = await _ruleEditor.SaveEditorAsync(draft, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

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
            CurrentRuleId is long currentRuleId
                ? Rules.FirstOrDefault(rule => rule.Id == currentRuleId)?.IsEnabled ?? DraftIsEnabled
                : DraftIsEnabled,
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
