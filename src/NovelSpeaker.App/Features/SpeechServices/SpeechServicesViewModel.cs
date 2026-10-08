using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Presentation.Workbenches;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Activation;
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
    private readonly WorkbenchExchangeInteraction _exchange;
    private readonly WorkbenchImportSession _import = new();
    private readonly BatchDeleteSession _batchDelete = new();
    private readonly IUiScheduler _scheduler;
    private PageActivationScope? _activation;
    private Task _deactivationDrain = Task.CompletedTask;
    private readonly LatestOperationSlot _providerRefresh = new();
    private readonly LatestOperationSlot _testPreparation = new();
    private readonly LatestOperationSlot _voiceEditor = new();
    private readonly LatestOperationSlot _voiceLoad = new();
    private readonly LatestOperationSlot _voiceFilter = new();
    private readonly EditorSession<ProviderId?, HttpEditorDraft> _editor = new(DraftsEqual);
    private readonly ResettableObservableCollection<SpeechProviderListItemViewModel> _providers = [];
    private readonly ManagementSelectionController<ProviderId> _selection = new();
    private IReadOnlyList<SpeechProviderInstance> _completeOrder = [];
    private SpeechProviderInstance? _editingProvider;
    private bool _transitionBusy;
    private readonly EdgeVoiceCatalog _voiceCatalog;
    private readonly EditorSession<ProviderId?, EdgeEditorDraft> _edgeEditor = new((left, right) => left == right);
    private readonly ResettableObservableCollection<EdgeVoice> _voices = [];
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
        _exchange = new(documents);
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

    public void HandleNavigatedTo(PageActivationScope activation)
    {
        HandleNavigatedFrom();
        _activation = activation;
        activation.Register(() =>
        {
            if (ReferenceEquals(_activation, activation)) HandleNavigatedFrom();
        });
        _settings.Changed += OnSettingsChanged;
        activation.Register(() => _settings.Changed -= OnSettingsChanged);
        _preview.PlaybackFailed += OnPreviewFailed;
        activation.Register(() => _preview.PlaybackFailed -= OnPreviewFailed);
    }

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_activation?.IsCurrent != true)
            HandleNavigatedTo(new PageActivationController().Activate());
        return RefreshAsync(cancellationToken);
    }

    public void HandleNavigatedFrom()
    {
        if (_activation is not { } activation) return;
        _activation = null;
        activation.Dispose();
        _providerRefresh.Cancel();
        _testPreparation.Cancel();
        IsTestBusy = false;
        _selection.Reset();
        CancelVoiceWork();
        IsHelpDrawerOpen = false;
        IsVoicePickerOpen = false;
        _deactivationDrain = activation.WaitForPendingOperationsAsync();
    }

    public async Task FinishDeactivationAsync()
    {
        var drain = _deactivationDrain;
        if (_activation is null && TestDraftCommand.ExecutionTask is { } test)
        {
            try { await test; }
            catch (OperationCanceledException) { }
        }
        // Preview audio has its own owner; stopping it is an explicit navigation action.
        if (_activation is null) await _preview.StopAsync(CancellationToken.None);
        await drain;
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
        var draft = await _workspace.CreateDraftAsync(token);
        token.ThrowIfCancellationRequested();
        OpenEditor(draft, true);
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
            token.ThrowIfCancellationRequested();
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
                if (!await _selection.TryEnterAsync(() => IsBusy, ConfirmLeaveAsync, token)) return;
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
        var execution = await _import.RunAsync(readDocument, async (document, importToken) =>
        {
            var result = await _workspace.ImportAsync(document.Json, importToken);
            await RefreshAsync(importToken);
            return result;
        }, () => IsBusy, value => IsBusy = value, token);
        if (execution is null) return;
        var imported = execution.Result;
        if (imported.Error is not null) _feedback.ShowWarning("无法导入语音服务", imported.Error);
        else _feedback.ShowSuccess("导入完成",
            $"已导入 {imported.ImportedCount} 项，跳过重复 {imported.DuplicateCount} 项，失败 {imported.FailedCount} 项。");
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
                if (await _exchange.WriteAsync(_ => Task.FromResult<string?>(result.Json),
                        clipboard, "speech-provider.json", token) != ExchangeWriteResult.Completed) return;
                var message = $"成功 {result.ExportedCount}，跳过 {result.SkippedCount}，失败 0。";
                if (result.SkippedCount > 0) _feedback.ShowWarning("导出完成", message);
                else _feedback.ShowSuccess("导出完成", message);
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
            if (!WorkbenchReorderController.TryMoveToSlot(complete, visible, source.Id, request.SlotIndex, out var order)) return;
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
            token.ThrowIfCancellationRequested();
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
        var operation = _testPreparation.Begin(token, _activation);
        IsTestBusy = true;
        await operation.RunAsync(async current =>
        {
            try
            {
                var failure = await _preview.PlayAsync(draft!, current.CancellationToken);
                current.TryCommit(() =>
                {
                    if (failure is null) _feedback.ShowSuccess("试听已开始", "正在播放编辑副本。");
                    else _feedback.ShowWarning("试听失败", failure.Message);
                });
            }
            finally
            {
                if (ReferenceEquals(_testPreparation.Current, current) && _activation?.IsCurrent == true)
                    IsTestBusy = false;
            }
        }, _ => _feedback.ShowWarning("试听失败", "试听未完成，请重试。"));
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

    public Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return Task.FromResult(false);
        Task<UnsavedChangesDecision> Decide(CancellationToken token) =>
            _dialogs.ShowUnsavedChangesAsync("未保存的修改", "语音服务有未保存的修改。要先保存再继续吗？",
                "保存", "放弃", "取消", token);
        Task Discard(CancellationToken token)
        {
            if (IsEditingNewProvider) CloseEditor();
            else if (_editingProvider is not null) OpenEditor(_editingProvider, false);
            return Task.CompletedTask;
        }
        return IsEdgeEditor
            ? _edgeEditor.ConfirmLeaveAsync(Decide, SaveDraftCoreAsync, Discard, cancellationToken)
            : _editor.ConfirmLeaveAsync(Decide, SaveDraftCoreAsync, Discard, cancellationToken);
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
            cancellationToken.ThrowIfCancellationRequested();
            OpenEditor(saved, false);
            _feedback.ShowSuccess("语音服务已保存", "配置已保存。");
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) return false;
            _feedback.ShowWarning("无法保存语音服务", exception is InvalidOperationException
                ? exception.Message : "保存未完成，请重试。草稿已保留。");
            return false;
        }
        finally { IsBusy = false; }
    }

    private Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_activation is not { IsCurrent: true } activation) return Task.CompletedTask;
        var operation = _providerRefresh.Begin(cancellationToken, activation);
        var task = RefreshCoreAsync(operation);
        activation.Register(task);
        return task;
    }

    private async Task RefreshCoreAsync(LatestOperationSlot.Operation operation)
    {
        using (operation)
        {
            try
            {
                var all = await _store.GetAllAsync(operation.CancellationToken);
                operation.TryCommit(() =>
                {
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
                });
            }
            catch (Exception) when (!operation.IsCurrent) { }
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
        _testPreparation.Cancel();
        if (TestDraftCommand.ExecutionTask is { IsCompleted: false } test)
        {
            try { await test.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
        cancellationToken.ThrowIfCancellationRequested();
        IsTestBusy = false;
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

    private Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (_activation is not { IsCurrent: true } activation) return Task.CompletedTask;
        var task = RunCoreAsync(action, cancellationToken, activation);
        activation.Register(task);
        return task;
    }

    private async Task RunCoreAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken,
        PageActivationScope activation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, activation.CancellationToken);
        try { await action(linked.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (activation.IsCurrent && !linked.IsCancellationRequested)
                _feedback.ShowWarning("语音服务操作失败", "操作未完成，请重试。");
        }
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs args) => PostEvent(() =>
    {
        if (_editingProvider is not null && !_workspace.IsVisible(_editingProvider))
        {
            _testPreparation.Cancel();
            IsTestBusy = false;
            CloseEditor();
            _activation?.Run(token => _preview.StopAsync(token));
        }
        _activation?.Run(RefreshAsync);
    });
    private void OnPreviewFailed(object? sender, ProviderPreviewPlaybackFailedEventArgs args) =>
        PostEvent(() => _feedback.ShowWarning("试听失败", args.Message));
    private void PostEvent(Action action)
    {
        if (_activation is not { IsCurrent: true } activation) return;
        activation.Run(token => _scheduler.InvokeAsync(() => activation.TryCommit(action), token));
    }

    private void OpenEdgeEditor(ProviderId providerId, EdgeSpeechProviderConfiguration configuration)
    {
        _voiceEditor.Begin(activation: _activation);
        DraftVoice = configuration.Voice;
        _edgeEditor.Open(providerId, new EdgeEditorDraft(DraftVoice), false, null);
        _activation?.Run(_ => FilterVoicesAsync());
        _activation?.Run(_ => LoadVoicesAsync(false));
    }

    private void CancelVoiceWork()
    {
        _voiceEditor.Cancel();
        _voiceLoad.Cancel();
        _voiceFilter.Cancel();
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
            _activation?.Run(_ => LoadVoicesAsync(false));
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

    private Task LoadVoicesAsync(bool refresh)
    {
        if (!IsEdgeEditor || _voiceEditor.Current is not { IsCurrent: true } editor ||
            _activation is not { IsCurrent: true } activation || IsVoiceCatalogLoading)
            return Task.CompletedTask;
        var operation = _voiceLoad.Begin(editor.CancellationToken, activation);
        IsVoiceCatalogLoading = true;
        var task = LoadVoicesCoreAsync(refresh, editor, operation);
        activation.Register(task);
        return task;
    }

    private async Task LoadVoicesCoreAsync(bool refresh, LatestOperationSlot.Operation editor,
        LatestOperationSlot.Operation operation)
    {
        using (operation)
        {
            try
            {
                await _voiceCatalog.GetAsync(refresh, operation.CancellationToken);
                await _scheduler.InvokeAsync(async () =>
                {
                    if (!editor.IsCurrent || !operation.TryCommit(() =>
                        {
                            _hasLoadedCatalog = true;
                            UpdateMissingVoiceMessage();
                        })) return;
                    await FilterVoicesAsync();
                }, operation.CancellationToken);
            }
            catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested) { }
            catch (Exception)
            {
                await _scheduler.InvokeAsync(() => operation.TryCommit(() =>
                    VoiceCatalogMessage = "无法加载 Voice 列表，请稍后重试。现有配置已保留。"));
            }
            finally
            {
                await _scheduler.InvokeAsync(() => operation.TryCommit(() => IsVoiceCatalogLoading = false));
            }
        }
    }

    private async Task FilterVoicesAsync()
    {
        if (!IsEdgeEditor || _voiceEditor.Current is not { IsCurrent: true } editor ||
            _activation is not { IsCurrent: true } activation) return;
        var operation = _voiceFilter.Begin(editor.CancellationToken, activation);
        var search = VoiceSearch.Trim();
        var catalog = _voiceCatalog.Current;
        await operation.RunAsync(async current =>
        {
            var matches = await Task.Run(() => catalog.Where(voice =>
            {
                current.CancellationToken.ThrowIfCancellationRequested();
                return voice.FriendlyName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       voice.Locale.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       voice.VoiceId.Contains(search, StringComparison.OrdinalIgnoreCase);
            }).ToArray(), current.CancellationToken);
            await _scheduler.InvokeAsync(() => current.TryCommit(() => _voices.ReplaceWith(matches)),
                current.CancellationToken);
        });
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

    partial void OnVoiceSearchChanged(string value) => _activation?.Run(_ => FilterVoicesAsync());
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
        await _selection.TryEnterAsync(() => IsBusy, ConfirmLeaveAsync, token);
    }, cancellationToken);

    [RelayCommand]
    private void CancelManagement() => _selection.Exit();

    [RelayCommand]
    private void SelectAll() => _selection.SelectAll();

    public void HandleProviderRightClick(SpeechProviderListItemViewModel item) => _selection.HandleRightClick(item.Id);

    private async Task DeleteSelectedProvidersAsync(CancellationToken cancellationToken)
    {
        if (_selection.SelectedCount == 0) return;
        var result = await _batchDelete.RunAsync<SpeechProviderListItemViewModel>(
            async token =>
            {
                if (!await ConfirmLeaveAsync(token)) return [];
                var items = Providers.Where(item => _selection.IsSelected(item.Id)).ToArray();
                if (items.Length == 0 || await _feedback.ConfirmDeletionAsync("删除语音服务",
                        $"将删除所选的 {items.Length} 项语音服务，此操作不可撤销。", token)
                    != AppConfirmationDecision.Confirm) return [];
                await StopTestAsync(token);
                return items;
            },
            async (item, token) =>
            {
                if (!item.CanDelete) return false;
                await _workspace.DeleteAsync(item.Id, token);
                if (_editingProvider?.Id == item.Id) CloseEditor();
                return true;
            },
            RefreshAsync, value => IsBusy = value, cancellationToken);
        if (result is null) return;
        var message = $"成功 {result.Succeeded}，跳过 {result.Skipped}，失败 {result.Failed}。";
        if (result.Skipped > 0 || result.Failed > 0) _feedback.ShowWarning("删除完成", message);
        else _feedback.ShowSuccess("删除完成", message);
    }
}
