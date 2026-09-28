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
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.App.Features.SpeechServices;

/// <summary>Owns the page's editing selection and HTTP draft; CurrentProvider remains settings-owned.</summary>
public sealed partial class SpeechServicesViewModel : ObservableObject, ITransientEscapeHandler
{
    private readonly IProviderStore _store;
    private readonly SpeechProviderWorkspace _workspace;
    private readonly HttpProviderDraftPreviewService _preview;
    private readonly IAppSettingsService _settings;
    private readonly IAppFeedbackService _feedback;
    private readonly IAppDialogService _dialogs;
    private readonly IAppNavigator _navigator;
    private readonly IRuleDocumentInteraction _documents;
    private readonly IUiScheduler _scheduler;
    private readonly OwnedTaskRegistry _eventTasks = new();
    private readonly EditorSession<ProviderId?, HttpEditorDraft> _editor = new(DraftsEqual);
    private readonly ResettableObservableCollection<SpeechProviderListItemViewModel> _providers = [];
    private IReadOnlyList<SpeechProviderInstance> _completeOrder = [];
    private SpeechProviderInstance? _editingProvider;
    private CancellationTokenSource? _pageCts;
    private CancellationToken _pageToken;
    private CancellationTokenSource? _testCts;
    private bool _transitionBusy;
    private bool _isActive;

    public SpeechServicesViewModel(IProviderStore store, SpeechProviderWorkspace workspace,
        HttpProviderDraftPreviewService preview, IAppSettingsService settings,
        IAppFeedbackService feedback, IAppDialogService dialogs, IAppNavigator navigator,
        IRuleDocumentInteraction documents, IUiScheduler scheduler)
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

    public bool HasEditor => _editingProvider is not null;
    public bool IsHttpEditor => _editingProvider?.Type == SpeechProviderType.Http;
    public bool IsEditingNewProvider => _editor.IsNew;
    public bool HasUnsavedChanges => _editor.IsDirty;
    public bool CanManage => !IsBusy && !_transitionBusy;
    public bool CanSaveDraft => IsHttpEditor && HasUnsavedChanges && CanManage;
    public bool CanCancelEditing => HasEditor && CanManage;
    public bool CanTestDraft => IsHttpEditor && CanManage && !IsTestBusy;
    public bool IsPostMethod => DraftRequestMethod == "POST";

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
        _isActive = false;
        _settings.Changed -= OnSettingsChanged;
        _preview.PlaybackFailed -= OnPreviewFailed;
        _pageCts?.Cancel();
        _testCts?.Cancel();
        IsHelpDrawerOpen = false;
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
            if (item is null || (SelectedProviderId == item.Id && !IsEditingNewProvider)) return;
            if (!await ConfirmLeaveAsync(token)) return;
            var provider = await _store.GetByIdAsync(item.Id, token);
            token.ThrowIfCancellationRequested();
            await StopTestAsync(token);
            if (provider is null) CloseEditor();
            else OpenEditor(provider, false);
        }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task ImportProvidersAsync(CancellationToken cancellationToken) => TransitionAsync(async token =>
    {
        var document = await _documents.PickImportAsync(token);
        if (document is null || !await ConfirmLeaveAsync(token)) return;
        await StopTestAsync(token);
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
            if (item?.CanShare != true || !CanManage) return;
            IsBusy = true;
            try
            {
                var warning = await _workspace.ExportAsync(item.Id, false, token);
                if (warning.Status != ProviderExportStatus.ConfirmationRequired)
                {
                    _feedback.ShowWarning("无法导出语音服务", warning.Message);
                    return;
                }
                if (await _dialogs.ShowConfirmationAsync("导出凭据提示", warning.Message,
                        "继续导出", "取消", token) != AppConfirmationDecision.Confirm) return;
                var result = await _workspace.ExportAsync(item.Id, true, token);
                if (result.Status != ProviderExportStatus.Ready || result.Json is null)
                {
                    _feedback.ShowWarning("无法导出语音服务", result.Message);
                    return;
                }
                if (clipboard) await _documents.CopyAsync(result.Json, token);
                else await _documents.ExportAsync("speech-provider.json", result.Json, token);
            }
            finally { IsBusy = false; }
        }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task DeleteProviderAsync(SpeechProviderListItemViewModel? item, CancellationToken cancellationToken) =>
        TransitionAsync(async token =>
        {
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
            if (IsHttpEditor) _feedback.ShowWarning("无法试听", error ?? "编辑器不可用。");
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
        if (!IsHttpEditor || IsBusy) return false;
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
            OpenEditor(saved, false);
            await RefreshAsync(cancellationToken);
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
        var visible = _completeOrder;
        _providers.ReplaceWith(visible, provider => new SpeechProviderListItemViewModel(provider.Id, provider.Name,
            provider.Type, _settings.Current.CurrentProviderId == provider.Id, SelectedProviderId == provider.Id));
        for (var index = 0; index < Providers.Count; index++)
        {
            Providers[index].CanMoveUp = index > 0;
            Providers[index].CanMoveDown = index < Providers.Count - 1;
        }
    }

    private void OpenEditor(SpeechProviderInstance provider, bool isNew)
    {
        _editor.Close();
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
        foreach (var item in Providers) item.IsSelected = item.Id == SelectedProviderId;
        NotifyState();
    }

    private void CloseEditor()
    {
        _editor.Close();
        _editingProvider = null;
        SelectedProviderId = null;
        IsHelpDrawerOpen = false;
        foreach (var item in Providers) item.IsSelected = false;
        foreach (var entry in HeaderEntries) entry.PropertyChanged -= OnHeaderChanged;
        HeaderEntries.Clear();
        NotifyState();
    }

    private bool TryBuildProvider(out SpeechProviderInstance? provider, out string? error)
    {
        provider = null;
        error = null;
        if (_editingProvider is null || !IsHttpEditor) { error = "编辑器不可用。"; return false; }
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
        foreach (var property in new[] { nameof(HasEditor), nameof(IsHttpEditor), nameof(IsEditingNewProvider),
                     nameof(HasUnsavedChanges), nameof(CanManage), nameof(CanSaveDraft), nameof(CanCancelEditing), nameof(CanTestDraft) })
            OnPropertyChanged(property);
        NewProviderCommand.NotifyCanExecuteChanged();
        SelectProviderCommand.NotifyCanExecuteChanged();
        ImportProvidersCommand.NotifyCanExecuteChanged();
        ExportProviderCommand.NotifyCanExecuteChanged();
        CopyProviderCommand.NotifyCanExecuteChanged();
        ExportProviderToClipboardCommand.NotifyCanExecuteChanged();
        DeleteProviderCommand.NotifyCanExecuteChanged();
        ReorderProviderCommand.NotifyCanExecuteChanged();
        MoveProviderUpCommand.NotifyCanExecuteChanged();
        MoveProviderDownCommand.NotifyCanExecuteChanged();
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
        foreach (var item in Providers) item.IsCurrent = _settings.Current.CurrentProviderId == item.Id;
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

    private sealed record HttpEditorDraft(string Name, string Url, string Method, string Body,
        string MaxRequests, string WindowMilliseconds, IReadOnlyList<KeyValuePair<string, string>> Headers);
}
