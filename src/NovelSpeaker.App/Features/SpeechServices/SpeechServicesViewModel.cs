using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.App.Features.Rules.Shared;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.App.Features.SpeechServices;

/// <summary>Owns the page's editing selection and drafts; CurrentProvider remains settings-owned.</summary>
public sealed partial class SpeechServicesViewModel : ObservableObject, ITransientEscapeHandler
{
    private readonly IProviderStore _store;
    private readonly SpeechProviderWorkspace _workspace;
    private readonly ProviderDraftPreviewService _preview;
    private readonly IAppSettingsService _settings;
    private readonly IAppFeedbackService _feedback;
    private readonly IAppDialogService _dialogs;
    private readonly IAppNavigator _navigator;
    private readonly IRuleDocumentInteraction _documents;
    private readonly IUiScheduler _scheduler;
    private readonly OwnedTaskRegistry _eventTasks = new();
    private readonly EditorSession<ProviderId?, HttpEditorDraft> _editor = new(DraftsEqual);
    private readonly ResettableObservableCollection<SpeechProviderListItemViewModel> _providers = [];
    private readonly ManagementSelectionController<ProviderId> _selection = new();
    private IReadOnlyList<SpeechProviderInstance> _completeOrder = [];
    private SpeechProviderInstance? _editingProvider;
    private CancellationTokenSource? _pageCts;
    private CancellationToken _pageToken;
    private CancellationTokenSource? _testCts;
    private bool _transitionBusy;
    private bool _isActive;
    private readonly EdgeVoiceCatalog _voiceCatalog;
    private readonly EditorSession<ProviderId?, EdgeEditorDraft> _edgeEditor = new((left, right) => left == right);
    private readonly ResettableObservableCollection<EdgeVoice> _voices = [];
    private CancellationTokenSource? _voiceCts;
    private int _voiceEditorVersion;
    private int _voiceSearchVersion;
    private bool _hasLoadedCatalog;

    public SpeechServicesViewModel(IProviderStore store, SpeechProviderWorkspace workspace,
        ProviderDraftPreviewService preview, IAppSettingsService settings,
        IAppFeedbackService feedback, IAppDialogService dialogs, IAppNavigator navigator,
        IRuleDocumentInteraction documents, IUiScheduler scheduler, EdgeVoiceCatalog voiceCatalog)
    {
        _store = store;
        _workspace = workspace;
        _preview = preview;
        _settings = settings;
        _feedback = feedback;
        _dialogs = dialogs;
        _navigator = navigator;
        _documents = documents;
        _scheduler = scheduler;
        _voiceCatalog = voiceCatalog;
        _selection.StateChanged += (_, _) =>
        {
            SyncSelection();
            OnPropertyChanged(nameof(IsManagementMode));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(CanReorder));
        };
    }

    public ObservableCollection<SpeechProviderListItemViewModel> Providers => _providers;
    public ObservableCollection<EditableKeyValueItemViewModel> HeaderEntries { get; } = [];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isTestBusy;
    [ObservableProperty] private bool isHelpDrawerOpen;
    [ObservableProperty] private ProviderId? selectedProviderId;
    [ObservableProperty] private string draftName = string.Empty;
    [ObservableProperty] private string draftUrl = string.Empty;
    [ObservableProperty] private string draftRequestMethod = "GET";
    [ObservableProperty] private string draftRequestBody = string.Empty;
    [ObservableProperty] private string draftMaxRequests = string.Empty;
    [ObservableProperty] private string draftWindowMilliseconds = string.Empty;
    [ObservableProperty] private EdgeVoice? draftVoice;
    [ObservableProperty] private bool isVoicePickerOpen;
    [ObservableProperty] private bool isVoiceCatalogLoading;
    [ObservableProperty] private string voiceSearch = string.Empty;
    [ObservableProperty] private string voiceCatalogMessage = string.Empty;

    public bool IsEdgeEditor => _editingProvider?.Type == SpeechProviderType.MicrosoftEdge;
    public bool CanRefreshVoices => IsEdgeEditor && CanManage && !IsVoiceCatalogLoading;
    public ObservableCollection<EdgeVoice> Voices => _voices;
    public string VoiceDisplayName => DraftVoice?.FriendlyName ?? "尚未选择 Voice";
    public string VoiceLocaleAndGender => DraftVoice is { } voice ? $"{voice.Locale} · {voice.Gender}" : string.Empty;
    public string VoiceId => DraftVoice?.VoiceId ?? string.Empty;

    public bool HasEditor => _editingProvider is not null;
    public bool IsHttpEditor => _editingProvider?.Type == SpeechProviderType.Http;
    public bool IsEditingNewProvider => _editor.IsNew;
    public bool HasUnsavedChanges => IsEdgeEditor ? _edgeEditor.IsDirty : _editor.IsDirty;
    public bool CanManage => !IsBusy && !_transitionBusy;
    public bool CanSaveDraft => HasEditor && HasUnsavedChanges && CanManage;
    public bool CanCancelEditing => HasEditor && CanManage;
    public bool CanTestDraft => (IsHttpEditor || IsEdgeEditor && DraftVoice is not null) && CanManage && !IsTestBusy;
    public bool IsPostMethod => DraftRequestMethod == "POST";

    public bool IsManagementMode => _selection.IsManagementMode;
    public int SelectedCount => _selection.SelectedCount;
    public bool CanReorder => CanManage && !IsManagementMode;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _pageCts?.Dispose();
        _pageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pageToken = _pageCts.Token;
        if (!_isActive)
        {
            _isActive = true;
            _settings.Changed += OnSettingsChanged;
            _preview.PlaybackFailed += OnPreviewFailed;
        }

        await RefreshAsync(cancellationToken);
    }

    public void HandleNavigatedFrom()
    {
        _selection.Reset();
        _isActive = false;
        _settings.Changed -= OnSettingsChanged;
        _preview.PlaybackFailed -= OnPreviewFailed;
        _pageCts?.Cancel();
        _testCts?.Cancel();
        CancelVoiceWork();
        IsHelpDrawerOpen = false;
        IsVoicePickerOpen = false;
    }

    public async Task FinishDeactivationAsync()
    {
        if (TestDraftCommand.ExecutionTask is { } test)
        {
            try { await test; }
            catch (OperationCanceledException) { }
        }
        await _preview.StopAsync(CancellationToken.None);
        await _eventTasks.WaitForCompletionAsync();
        _pageCts?.Dispose();
        _pageCts = null;
    }

    public bool TryHandleEscape()
    {
        if (_selection.Exit()) return true;
        if (IsVoicePickerOpen) { IsVoicePickerOpen = false; return true; }
        if (!IsHelpDrawerOpen) return false;
        IsHelpDrawerOpen = false;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task NewProviderAsync(CancellationToken cancellationToken) => TransitionAsync(async token =>
    {
        if (!await ConfirmLeaveAsync(token)) return;
        await StopTestAsync(token);
        OpenEditor(await _workspace.CreateDraftAsync(token), true);
    }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task SelectProviderAsync(SpeechProviderListItemViewModel? item, CancellationToken cancellationToken) =>
        TransitionAsync(async token =>
        {
            if (item is null) return;
            if (_selection.HandleClick(item.Id)) return;
            if (SelectedProviderId == item.Id && !IsEditingNewProvider)
            {
                SyncSelection();
                return;
            }
            if (!await ConfirmLeaveAsync(token)) return;
            var provider = await _store.GetByIdAsync(item.Id, token);
            token.ThrowIfCancellationRequested();
            await StopTestAsync(token);
            if (provider is null) CloseEditor();
            else OpenEditor(provider, false);
        }, cancellationToken);

    public async Task SelectProviderWithModifiersAsync(SpeechProviderListItemViewModel? item,
        DesktopSelectionModifiers modifiers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item is null || !CanManage) return;
        if (IsManagementMode || modifiers != DesktopSelectionModifiers.None)
        {
            await TransitionAsync(async token =>
            {
                if (!IsManagementMode && !await ConfirmLeaveAsync(token)) return;
                token.ThrowIfCancellationRequested();
                _selection.HandleClick(item.Id, modifiers);
            }, cancellationToken);
            return;
        }
        await SelectProviderAsync(item, cancellationToken);
    }

    private void SyncSelection()
    {
        foreach (var item in Providers)
            item.IsSelected = IsManagementMode ? _selection.IsSelected(item.Id) : item.Id == SelectedProviderId;
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task ImportProvidersAsync(CancellationToken cancellationToken) =>
        ImportAsync(_documents.PickImportAsync, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task ImportProvidersFromClipboardAsync(CancellationToken cancellationToken) =>
        ImportAsync(_documents.ReadClipboardAsync, cancellationToken);

    private Task ImportAsync(Func<CancellationToken, Task<RuleImportDocument?>> readDocument,
        CancellationToken cancellationToken) => TransitionAsync(async token =>
    {
        var document = await readDocument(token);
        if (document is null) return;
        IsBusy = true;
        try
        {
            var result = await _workspace.ImportAsync(document.Json, token);
            await RefreshAsync(token);
            if (result.Error is not null) _feedback.ShowWarning("无法导入语音服务", result.Error);
            else _feedback.ShowSuccess("导入完成",
                $"已导入 {result.ImportedCount} 项，跳过重复 {result.DuplicateCount} 项，失败 {result.FailedCount} 项。");
        }
        finally { IsBusy = false; }
    }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task ExportProviderAsync(SpeechProviderListItemViewModel? item, CancellationToken cancellationToken) =>
        ExportAsync(item, false, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task CopyProviderAsync(SpeechProviderListItemViewModel? item, CancellationToken cancellationToken) =>
        RunAsync(async token =>
        {
            if (item?.CanShare != true || !CanManage) return;
            IsBusy = true;
            try
            {
                await _workspace.CopyAsync(item.Id, token);
                await RefreshAsync(token);
            }
            finally { IsBusy = false; }
        }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task ExportProviderToClipboardAsync(SpeechProviderListItemViewModel? item, CancellationToken cancellationToken) =>
        ExportAsync(item, true, cancellationToken);

    private Task ExportAsync(SpeechProviderListItemViewModel? item, bool clipboard, CancellationToken cancellationToken) =>
        RunAsync(async token =>
        {
            if ((!IsManagementMode && item?.CanShare != true) || !CanManage) return;
            IsBusy = true;
            try
            {
                var ids = IsManagementMode ? _selection.SelectedItems.ToArray() : new[] { item!.Id };
                if (ids.Length == 0) return;
                var warning = await _workspace.ExportAsync(ids, false, token);
                if (warning.Status != ProviderExportStatus.ConfirmationRequired)
                {
                    _feedback.ShowWarning("无法导出语音服务", $"成功 0，跳过 {warning.SkippedCount}，失败 0。{warning.Message}");
                    return;
                }
                if (await _dialogs.ShowConfirmationAsync("导出凭据提示", warning.Message,
                        "继续导出", "取消", token) != AppConfirmationDecision.Confirm) return;
                var result = await _workspace.ExportAsync(ids, true, token);
                if (result.Status != ProviderExportStatus.Ready || result.Json is null)
                {
                    _feedback.ShowWarning("无法导出语音服务", result.Message);
                    return;
                }
                if (clipboard) await _documents.CopyAsync(result.Json, token);
                else if (!await _documents.ExportAsync("speech-provider.json", result.Json, token)) return;
                _feedback.ShowSuccess("导出完成", $"成功 {result.ExportedCount}，跳过 {result.SkippedCount}，失败 0。");
            }
            finally { IsBusy = false; }
        }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task DeleteProviderAsync(SpeechProviderListItemViewModel? item, CancellationToken cancellationToken) =>
        TransitionAsync(async token =>
        {
            if (IsManagementMode)
            {
                await DeleteSelectedProvidersAsync(token);
                return;
            }
            if (item?.CanDelete != true || !await ConfirmLeaveAsync(token)) return;
            if (await _feedback.ConfirmDeletionAsync("删除语音服务", $"将删除语音服务“{item.Name}”。此操作不可撤销。",
                    token) != AppConfirmationDecision.Confirm) return;
            await StopTestAsync(token);
            IsBusy = true;
            try
            {
                await _workspace.DeleteAsync(item.Id, token);
                if (_editingProvider?.Id == item.Id) CloseEditor();
                await RefreshAsync(token);
            }
            finally { IsBusy = false; }
        }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task ReorderProviderAsync(RuleReorderRequest? request, CancellationToken cancellationToken) =>
        RunAsync(async token =>
        {
            if (request?.Source is not SpeechProviderListItemViewModel source || !CanManage) return;
            var complete = _completeOrder.Select(provider => provider.Id).ToArray();
            var visible = Providers.Select(provider => provider.Id).ToArray();
            if (!RuleReorderController.TryMoveToSlot(complete, visible, source.Id, request.SlotIndex, out var order)) return;
            IsBusy = true;
            try
            {
                await _workspace.ReorderAsync(order, token);
                await RefreshAsync(token);
            }
            finally { IsBusy = false; }
        }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task MoveProviderUpAsync(SpeechProviderListItemViewModel? item, CancellationToken cancellationToken) =>
        MoveAsync(item, -1, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task MoveProviderDownAsync(SpeechProviderListItemViewModel? item, CancellationToken cancellationToken) =>
        MoveAsync(item, 1, cancellationToken);

    private Task MoveAsync(SpeechProviderListItemViewModel? item, int offset, CancellationToken cancellationToken)
    {
        var index = item is null ? -1 : Providers.IndexOf(item);
        if (index < 0 || index + offset < 0 || index + offset >= Providers.Count) return Task.CompletedTask;
        return ReorderProviderAsync(new RuleReorderRequest(item!, offset < 0 ? index - 1 : index + 2), cancellationToken);
    }

    [RelayCommand]
    private Task SaveDraftAsync(CancellationToken cancellationToken) =>
        RunAsync(async token => { await SaveDraftCoreAsync(token); }, cancellationToken);

    [RelayCommand]
    private Task CancelEditingAsync(CancellationToken cancellationToken) =>
        RunAsync(async token =>
        {
            if (!CanCancelEditing) return;
            await StopTestAsync(token);
            CloseEditor();
        }, cancellationToken);

    [RelayCommand]
    private Task TestDraftAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        if (!CanTestDraft) return;
        if (!TryBuildProvider(out var draft, out var error))
        {
            _feedback.ShowWarning("无法试听", error ?? "编辑器不可用。");
            return;
        }
        using var testCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _testCts = testCts;
        IsTestBusy = true;
        try
        {
            var failure = await _preview.PlayAsync(draft!, testCts.Token);
            testCts.Token.ThrowIfCancellationRequested();
            if (failure is null) _feedback.ShowSuccess("试听已开始", "正在播放编辑副本。");
            else _feedback.ShowWarning("试听失败", failure.Message);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_testCts, testCts)) _testCts = null;
            IsTestBusy = false;
        }
    }, cancellationToken);

    [RelayCommand] private void OpenHelp() { if (IsHttpEditor) IsHelpDrawerOpen = true; }
    [RelayCommand] private void CloseHelp() => IsHelpDrawerOpen = false;
    [RelayCommand]
    private void AddHeaderEntry()
    {
        if (!IsHttpEditor || IsBusy) return;
        var entry = new EditableKeyValueItemViewModel();
        entry.PropertyChanged += OnHeaderChanged;
        HeaderEntries.Add(entry);
        UpdateDirty();
    }
    [RelayCommand]
    private void RemoveHeaderEntry(EditableKeyValueItemViewModel? entry)
    {
        if (entry is null || IsBusy) return;
        entry.PropertyChanged -= OnHeaderChanged;
        HeaderEntries.Remove(entry);
        UpdateDirty();
    }
    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (await ConfirmLeaveAsync(cancellationToken))
            await _navigator.NavigateBackAsync(cancellationToken, bypassGuard: true);
    }

    public async Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        if (!HasUnsavedChanges) return true;
        var decision = await _dialogs.ShowUnsavedChangesAsync("未保存的修改", "语音服务有未保存的修改。要先保存再继续吗？",
            "保存", "放弃", "取消", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (decision == UnsavedChangesDecision.Save) return await SaveDraftCoreAsync(cancellationToken);
        if (decision != UnsavedChangesDecision.Discard) return false;
        if (IsEditingNewProvider) CloseEditor();
        else if (_editingProvider is not null) OpenEditor(_editingProvider, false);
        return true;
    }

    private async Task<bool> SaveDraftCoreAsync(CancellationToken cancellationToken)
    {
        if (!HasEditor || IsBusy) return false;
        if (!TryBuildProvider(out var draft, out var error))
        {
            _feedback.ShowWarning("无法保存语音服务", error!);
            return false;
        }
        IsBusy = true;
        try
        {
            await StopTestAsync(cancellationToken);
            var saved = await _workspace.SaveAsync(draft!, IsEditingNewProvider, cancellationToken);
            await RefreshAsync(cancellationToken);
            OpenEditor(saved, false);
            _feedback.ShowSuccess("语音服务已保存", "配置已保存。");
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _feedback.ShowWarning("无法保存语音服务", exception is InvalidOperationException
                ? exception.Message : "保存未完成，请重试。草稿已保留。");
            return false;
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var all = await _store.GetAllAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _completeOrder = all.OrderBy(provider => provider.SortOrder).ThenBy(provider => provider.Id.Value).ToArray();
        // Visibility is a projection of this complete, unified order.
        var visible = _completeOrder.Where(_workspace.IsVisible);
        _providers.ReplaceWith(visible, provider => new SpeechProviderListItemViewModel(provider.Id, provider.Name,
            provider.Type, _settings.Current.CurrentProviderId == provider.Id, false));
        _selection.SetItems(Providers.Select(item => item.Id));
        SyncSelection();
        for (var index = 0; index < Providers.Count; index++)
        {
            Providers[index].CanMoveUp = index > 0;
            Providers[index].CanMoveDown = index < Providers.Count - 1;
        }
    }

    private void OpenEditor(SpeechProviderInstance provider, bool isNew)
    {
        CancelVoiceWork();
        _editor.Close();
        _edgeEditor.Close();
        _editingProvider = provider;
        SelectedProviderId = isNew ? null : provider.Id;
        IsHelpDrawerOpen = false;
        foreach (var entry in HeaderEntries) entry.PropertyChanged -= OnHeaderChanged;
        HeaderEntries.Clear();
        DraftName = provider.Name;
        if (provider.Configuration is HttpSpeechProviderConfiguration http)
        {
            DraftUrl = http.UrlTemplate;
            DraftRequestMethod = http.Method;
            DraftRequestBody = http.BodyTemplate ?? string.Empty;
            DraftMaxRequests = http.RateLimit?.MaxRequests.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            DraftWindowMilliseconds = http.RateLimit?.WindowMilliseconds.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            foreach (var header in http.Headers)
            {
                var entry = new EditableKeyValueItemViewModel(header.Key, header.Value);
                entry.PropertyChanged += OnHeaderChanged;
                HeaderEntries.Add(entry);
            }
            _editor.Open(isNew ? null : provider.Id, CaptureDraft(), isNew, null);
        }
        else if (provider.Configuration is EdgeSpeechProviderConfiguration edge)
        {
            OpenEdgeEditor(provider.Id, edge);
        }
        SyncSelection();
        NotifyState();
    }

    private void CloseEditor()
    {
        CancelVoiceWork();
        _editor.Close();
        _edgeEditor.Close();
        _editingProvider = null;
        SelectedProviderId = null;
        IsHelpDrawerOpen = false;
        SyncSelection();
        foreach (var entry in HeaderEntries) entry.PropertyChanged -= OnHeaderChanged;
        HeaderEntries.Clear();
        NotifyState();
    }

    private bool TryBuildProvider(out SpeechProviderInstance? provider, out string? error)
    {
        provider = null;
        error = null;
        if (_editingProvider is null) { error = "编辑器不可用。"; return false; }
        if (IsEdgeEditor)
        {
            provider = _editingProvider with { Configuration = new EdgeSpeechProviderConfiguration(DraftVoice) };
            var edgeValidation = ProviderConfigurationValidator.Validate(provider);
            error = edgeValidation.IsValid ? null : string.Join(" ", edgeValidation.Errors);
            return edgeValidation.IsValid;
        }
        if (!IsHttpEditor) { error = "编辑器不可用。"; return false; }
        ProviderRequestRateLimit? rate = null;
        if (!string.IsNullOrWhiteSpace(DraftMaxRequests) || !string.IsNullOrWhiteSpace(DraftWindowMilliseconds))
        {
            if (!int.TryParse(DraftMaxRequests, NumberStyles.None, CultureInfo.InvariantCulture, out var max) || max <= 0 ||
                !int.TryParse(DraftWindowMilliseconds, NumberStyles.None, CultureInfo.InvariantCulture, out var window) || window <= 0)
            {
                error = "请求频率限制的次数和毫秒必须均为正整数，或同时留空。";
                return false;
            }
            rate = new ProviderRequestRateLimit(max, window);
        }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in HeaderEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) && string.IsNullOrWhiteSpace(entry.Value)) continue;
            if (!headers.TryAdd(entry.Key.Trim(), entry.Value)) { error = "Header 名称不能重复。"; return false; }
        }
        var originalHttp = (HttpSpeechProviderConfiguration)_editingProvider.Configuration;
        var body = IsPostMethod ? DraftRequestBody : null;
        if (body == string.Empty && originalHttp.BodyTemplate is null) body = null;
        provider = _editingProvider with
        {
            Name = DraftName.Trim(),
            Configuration = new HttpSpeechProviderConfiguration(DraftUrl.Trim(), DraftRequestMethod, headers,
                body, rate)
        };
        var validation = ProviderConfigurationValidator.Validate(provider);
        error = validation.IsValid ? null : string.Join(" ", validation.Errors);
        return validation.IsValid;
    }

    private HttpEditorDraft CaptureDraft() => new(DraftName, DraftUrl, DraftRequestMethod, DraftRequestBody,
        DraftMaxRequests, DraftWindowMilliseconds, HeaderEntries.Select(entry => new KeyValuePair<string, string>(entry.Key, entry.Value)).ToArray());
    private static bool DraftsEqual(HttpEditorDraft left, HttpEditorDraft right) =>
        left with { Headers = right.Headers } == right && left.Headers.SequenceEqual(right.Headers);
    private void UpdateDirty() { _editor.UpdateDirty(CaptureDraft()); NotifyState(); }
    private void OnHeaderChanged(object? sender, PropertyChangedEventArgs args) => UpdateDirty();
    partial void OnDraftNameChanged(string value) => UpdateDirty();
    partial void OnDraftUrlChanged(string value) => UpdateDirty();
    partial void OnDraftRequestMethodChanged(string value) { OnPropertyChanged(nameof(IsPostMethod)); UpdateDirty(); }
    partial void OnDraftRequestBodyChanged(string value) => UpdateDirty();
    partial void OnDraftMaxRequestsChanged(string value) => UpdateDirty();
    partial void OnDraftWindowMillisecondsChanged(string value) => UpdateDirty();
    partial void OnIsBusyChanged(bool value) => NotifyState();
    partial void OnIsTestBusyChanged(bool value) => NotifyState();

    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanReorder));
        foreach (var property in new[] { nameof(HasEditor), nameof(IsHttpEditor), nameof(IsEdgeEditor), nameof(IsEditingNewProvider),
                     nameof(HasUnsavedChanges), nameof(CanManage), nameof(CanSaveDraft), nameof(CanCancelEditing), nameof(CanTestDraft), nameof(CanRefreshVoices) })
            OnPropertyChanged(property);
        NewProviderCommand.NotifyCanExecuteChanged();
        EnterManagementCommand.NotifyCanExecuteChanged();
        SelectProviderCommand.NotifyCanExecuteChanged();
        ImportProvidersCommand.NotifyCanExecuteChanged();
        ImportProvidersFromClipboardCommand.NotifyCanExecuteChanged();
        ExportProviderCommand.NotifyCanExecuteChanged();
        CopyProviderCommand.NotifyCanExecuteChanged();
        ExportProviderToClipboardCommand.NotifyCanExecuteChanged();
        DeleteProviderCommand.NotifyCanExecuteChanged();
        ReorderProviderCommand.NotifyCanExecuteChanged();
        MoveProviderUpCommand.NotifyCanExecuteChanged();
        MoveProviderDownCommand.NotifyCanExecuteChanged();
        ChangeVoiceCommand.NotifyCanExecuteChanged();
        RefreshVoiceCatalogCommand.NotifyCanExecuteChanged();
        SelectVoiceCommand.NotifyCanExecuteChanged();
    }

    private async Task StopTestAsync(CancellationToken cancellationToken)
    {
        _testCts?.Cancel();
        if (TestDraftCommand.ExecutionTask is { IsCompleted: false } test)
        {
            try { await test.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
        await _preview.StopAsync(cancellationToken);
    }

    private Task TransitionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken) =>
        RunAsync(async token =>
        {
            if (!CanManage) return;
            _transitionBusy = true;
            NotifyState();
            try { await action(token); }
            finally { _transitionBusy = false; NotifyState(); }
        }, cancellationToken);

    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _pageCts?.Token ?? CancellationToken.None);
        try { await action(linked.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _feedback.ShowWarning("语音服务操作失败", "操作未完成，请重试。");
        }
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs args) => PostEvent(() =>
    {
        if (_editingProvider is not null && !_workspace.IsVisible(_editingProvider))
        {
            _testCts?.Cancel();
            CloseEditor();
            _eventTasks.Register(_preview.StopAsync(_pageToken));
        }
        _eventTasks.Register(RefreshAsync(_pageToken));
    });
    private void OnPreviewFailed(object? sender, ProviderPreviewPlaybackFailedEventArgs args) =>
        PostEvent(() => _feedback.ShowWarning("试听失败", args.Message));
    private void PostEvent(Action action)
    {
        if (!_isActive) return;
        var token = _pageToken;
        try
        {
            _eventTasks.Register(_scheduler.InvokeAsync(() => { if (!token.IsCancellationRequested) action(); }, token));
        }
        catch (Exception)
        {
            // Presentation delivery must not fail a settings mutation or background audio operation.
        }
    }

    private void OpenEdgeEditor(ProviderId providerId, EdgeSpeechProviderConfiguration configuration)
    {
        _voiceCts = CancellationTokenSource.CreateLinkedTokenSource(_pageToken);
        DraftVoice = configuration.Voice;
        _edgeEditor.Open(providerId, new EdgeEditorDraft(DraftVoice), false, null);
        _eventTasks.Register(FilterVoicesAsync());
        _eventTasks.Register(LoadVoicesAsync(false));
    }

    private void CancelVoiceWork()
    {
        _voiceEditorVersion++;
        _voiceSearchVersion++;
        _voiceCts?.Cancel();
        _voiceCts?.Dispose();
        _voiceCts = null;
        _hasLoadedCatalog = false;
        IsVoiceCatalogLoading = false;
        IsVoicePickerOpen = false;
        VoiceSearch = string.Empty;
        VoiceCatalogMessage = string.Empty;
        _voices.ReplaceWith([]);
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private void ChangeVoice()
    {
        if (!IsEdgeEditor) return;
        IsVoicePickerOpen = !IsVoicePickerOpen;
        if (IsVoicePickerOpen && !_hasLoadedCatalog && !IsVoiceCatalogLoading)
            _eventTasks.Register(LoadVoicesAsync(false));
    }

    [RelayCommand(CanExecute = nameof(CanRefreshVoices))]
    private Task RefreshVoiceCatalogAsync() => LoadVoicesAsync(true);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private void SelectVoice(EdgeVoice? voice)
    {
        if (!IsEdgeEditor || voice is null) return;
        DraftVoice = voice;
        IsVoicePickerOpen = false;
        VoiceCatalogMessage = string.Empty;
    }

    private async Task LoadVoicesAsync(bool refresh)
    {
        if (!IsEdgeEditor || _voiceCts is null || IsVoiceCatalogLoading) return;
        var token = _voiceCts.Token;
        var version = _voiceEditorVersion;
        IsVoiceCatalogLoading = true;
        try
        {
            await _voiceCatalog.GetAsync(refresh, token);
            token.ThrowIfCancellationRequested();
            await _scheduler.InvokeAsync(async () =>
            {
                if (version != _voiceEditorVersion) return;
                _hasLoadedCatalog = true;
                UpdateMissingVoiceMessage();
                await FilterVoicesAsync();
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
                await _scheduler.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested && version == _voiceEditorVersion)
                        VoiceCatalogMessage = "无法加载 Voice 列表，请稍后重试。现有配置已保留。";
                });
        }
        finally
        {
            if (!token.IsCancellationRequested)
                await _scheduler.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested && version == _voiceEditorVersion) IsVoiceCatalogLoading = false;
                });
        }
    }

    private async Task FilterVoicesAsync()
    {
        if (!IsEdgeEditor || _voiceCts is null) return;
        var token = _voiceCts.Token;
        var editorVersion = _voiceEditorVersion;
        var searchVersion = ++_voiceSearchVersion;
        var search = VoiceSearch.Trim();
        var catalog = _voiceCatalog.Current;
        try
        {
            var matches = await Task.Run(() => catalog.Where(voice =>
            {
                token.ThrowIfCancellationRequested();
                return voice.FriendlyName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       voice.Locale.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       voice.VoiceId.Contains(search, StringComparison.OrdinalIgnoreCase);
            }).ToArray(), token);
            await _scheduler.InvokeAsync(() =>
            {
                if (editorVersion == _voiceEditorVersion && searchVersion == _voiceSearchVersion)
                    _voices.ReplaceWith(matches);
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void UpdateMissingVoiceMessage() => VoiceCatalogMessage =
        _hasLoadedCatalog && DraftVoice is { } voice && !_voiceCatalog.Current.Any(item => item.VoiceId == voice.VoiceId)
            ? "当前 Voice 未出现在最新列表中" : string.Empty;

    partial void OnDraftVoiceChanged(EdgeVoice? value)
    {
        _edgeEditor.UpdateDirty(new EdgeEditorDraft(value));
        OnPropertyChanged(nameof(VoiceDisplayName));
        OnPropertyChanged(nameof(VoiceLocaleAndGender));
        OnPropertyChanged(nameof(VoiceId));
        UpdateMissingVoiceMessage();
        NotifyState();
    }

    partial void OnVoiceSearchChanged(string value) => _eventTasks.Register(FilterVoicesAsync());
    partial void OnIsVoiceCatalogLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefreshVoices));
        RefreshVoiceCatalogCommand.NotifyCanExecuteChanged();
    }

    private sealed record EdgeEditorDraft(EdgeVoice? Voice);

    private sealed record HttpEditorDraft(string Name, string Url, string Method, string Body,
        string MaxRequests, string WindowMilliseconds, IReadOnlyList<KeyValuePair<string, string>> Headers);
    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task EnterManagementAsync(CancellationToken cancellationToken) => TransitionAsync(async token =>
    {
        if (IsManagementMode || !await ConfirmLeaveAsync(token)) return;
        token.ThrowIfCancellationRequested();
        _selection.Enter();
    }, cancellationToken);

    [RelayCommand]
    private void CancelManagement() => _selection.Exit();

    [RelayCommand]
    private void SelectAll() => _selection.SelectAll();

    public void HandleProviderRightClick(SpeechProviderListItemViewModel item) => _selection.HandleRightClick(item.Id);

    private async Task DeleteSelectedProvidersAsync(CancellationToken cancellationToken)
    {
        if (_selection.SelectedCount == 0 || !await ConfirmLeaveAsync(cancellationToken)) return;
        var items = Providers.Where(item => _selection.IsSelected(item.Id)).ToArray();
        if (await _feedback.ConfirmDeletionAsync("删除语音服务", $"将删除所选的 {items.Length} 项语音服务，此操作不可撤销。", cancellationToken)
            != AppConfirmationDecision.Confirm) return;
        await StopTestAsync(cancellationToken);
        IsBusy = true;
        try
        {
            var succeeded = 0;
            var skipped = 0;
            var failed = 0;
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!item.CanDelete) { skipped++; continue; }
                try
                {
                    await _workspace.DeleteAsync(item.Id, cancellationToken);
                    succeeded++;
                    if (_editingProvider?.Id == item.Id) CloseEditor();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { failed++; }
            }
            await RefreshAsync(cancellationToken);
            _feedback.ShowSuccess("删除完成", $"成功 {succeeded}，跳过 {skipped}，失败 {failed}。");
        }
        finally { IsBusy = false; }
    }
}
