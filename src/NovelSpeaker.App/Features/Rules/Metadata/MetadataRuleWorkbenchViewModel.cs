using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books.Import;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Features.Rules.Shared;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Shell.Activation;

namespace NovelSpeaker.App.Features.Rules.Metadata;

/// <summary>Shared visual editing behavior for the two independent metadata rule stores.</summary>
public abstract partial class MetadataRuleWorkbenchViewModel : ObservableObject, ITransientEscapeHandler
{
    private readonly IAppNavigator _navigator;
    private readonly IAppDialogService _dialogs;
    private readonly IAppFeedbackService _feedback;
    private readonly IRuleDocumentInteraction _documents;
    private readonly ManagementSelectionController<string> _selection = new(StringComparer.Ordinal);
    private readonly EditorSession<string?, MetadataRuleState> _editorSession = new(
        (left, right) => left.Name == right.Name && left.Pattern == right.Pattern, newIsDirty: true);
    private readonly RuleImportSession _importSession = new();
    private MetadataRuleState? Original => _editorSession.IsNew ? null : _editorSession.Baseline;

    private PageActivationScope? _activation;
    private readonly BatchDeleteSession _batchDelete = new();
    private readonly WorkbenchExchangeInteraction _exchange;

    protected MetadataRuleWorkbenchViewModel(
        IAppNavigator navigator,
        IAppDialogService dialogs,
        IAppFeedbackService feedback,
        IRuleDocumentInteraction documents)
    {
        _navigator = navigator;
        _dialogs = dialogs;
        _feedback = feedback;
        _documents = documents;
        _exchange = new(_documents);
        _selection.StateChanged += (_, _) =>
        {
            SyncSelection();
            OnPropertyChanged(nameof(IsManagementMode));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(CanReorder));
        };
    }

    public abstract string PageTitle { get; }
    protected abstract string DocumentFileName { get; }
    protected abstract string DocumentRuleType { get; }
    protected abstract Task<IReadOnlyList<MetadataRuleState>> ReadAsync(CancellationToken cancellationToken);
    protected abstract Task WriteAsync(MetadataRuleState rule, CancellationToken cancellationToken);
    protected abstract Task RemoveAsync(string id, CancellationToken cancellationToken);
    protected abstract Task WriteOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken);

    public ObservableCollection<MetadataRuleRow> Rules { get; } = [];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string draftName = string.Empty;
    [ObservableProperty] private string draftPattern = string.Empty;
    [ObservableProperty] private string validationMessage = string.Empty;

    public bool HasEditor => _editorSession.HasEditor;
    public bool HasUnsavedChanges => _editorSession.IsDirty;

    public bool IsManagementMode => _selection.IsManagementMode;
    public int SelectedCount => _selection.SelectedCount;
    public bool CanReorder => !IsBusy && !IsManagementMode;

    public bool TryHandleEscape() => _selection.Exit();

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_activation?.IsCurrent != true)
        {
            HandleNavigatedTo(new PageActivationController().Activate());
            _activation!.Register(cancellationToken.Register(_activation.Dispose));
        }
        await RefreshAsync(null, cancellationToken);
    }

    public void HandleNavigatedFrom()
    {
        var activation = _activation;
        _activation = null;
        activation?.Dispose();
        _selection.Reset();
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (await ConfirmLeaveAsync(cancellationToken))
        {
            await _navigator.NavigateBackAsync(cancellationToken, bypassGuard: true);
        }
    }

    [RelayCommand]
    private async Task NewRuleAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken)) return;
        Open(new MetadataRuleState(string.Empty, string.Empty, string.Empty, 0, true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), isNew: true);
    }

    [RelayCommand]
    private async Task SelectRuleAsync(MetadataRuleRow? row, CancellationToken cancellationToken)
    {
        if (row is null || IsBusy) return;
        if (_selection.HandleClick(row.Id)) return;
        if (!_editorSession.IsNew && Original?.Id == row.Id)
        {
            SyncSelection();
            return;
        }
        if (!await ConfirmLeaveAsync(cancellationToken)) return;
        var current = Rules.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (current is not null) Open(current.State);
    }

    public async Task SelectRuleWithModifiersAsync(MetadataRuleRow? row, DesktopSelectionModifiers modifiers,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (row is null || IsBusy) return;
        if (IsManagementMode || modifiers != DesktopSelectionModifiers.None)
        {
            if (!IsManagementMode && !await TryEnterManagementAsync(cancellationToken)) return;
            _selection.HandleClick(row.Id, modifiers);
            return;
        }
        await SelectRuleAsync(row, cancellationToken);
    }

    [RelayCommand]
    private async Task SaveRuleAsync(CancellationToken cancellationToken) => await SaveCoreAsync(cancellationToken);

    private async Task<bool> SaveCoreAsync(CancellationToken cancellationToken)
    {
        if (!HasEditor || IsBusy) return false;
        var name = DraftName.Trim();
        var pattern = DraftPattern.Trim();
        if (name.Length == 0)
        {
            ValidationMessage = "请输入规则名称。";
            return false;
        }

        try
        {
            ImportMetadataExtractor.ValidatePattern(pattern);
        }
        catch (ArgumentException exception)
        {
            ValidationMessage = exception.Message;
            return false;
        }

        if (Rules.Any(row => row.Id != Original?.Id && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationMessage = "规则名称已存在。";
            return false;
        }

        try
        {
            IsBusy = true;
            var now = DateTimeOffset.UtcNow;
            var rule = new MetadataRuleState(
                Original?.Id ?? Guid.NewGuid().ToString("N"), name, pattern,
                Original?.SortOrder ?? (Rules.Count == 0 ? 10 : Rules.Max(row => row.State.SortOrder) + 10),
                Original?.IsEnabled ?? true, Original?.CreatedAt ?? now, now);
            await WriteAsync(rule, cancellationToken);
            await RefreshAsync(rule.Id, cancellationToken);
            ValidationMessage = string.Empty;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _feedback.ShowProjectedNotification("保存元数据规则失败", _feedback.Project(exception));
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelEditing()
    {
        if (_editorSession.IsNew)
        {
            CloseEditor();
        }
        else if (Original is not null)
        {
            Open(Original);
        }

        ValidationMessage = string.Empty;
    }

    [RelayCommand]
    private async Task ToggleRuleEnabledAsync(MetadataRuleRow? row, CancellationToken cancellationToken)
    {
        if (row is null || !await ConfirmLeaveAsync(cancellationToken)) return;
        var current = Rules.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (current is null) return;
        await ExecuteAsync(async () =>
        {
            await WriteAsync(current.State with { IsEnabled = !current.IsEnabled, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
            await RefreshAsync(Original?.Id, cancellationToken);
        }, cancellationToken);
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(MetadataRuleRow? row, CancellationToken cancellationToken)
    {
        if (IsManagementMode)
        {
            await DeleteSelectedRulesAsync(cancellationToken);
            return;
        }
        if (row is null || !await ConfirmLeaveAsync(cancellationToken)) return;
        if (await _feedback.ConfirmDeletionAsync("删除元数据规则", $"确定删除“{row.Name}”吗？", cancellationToken) != AppConfirmationDecision.Confirm) return;
        await ExecuteAsync(async () =>
        {
            await RemoveAsync(row.Id, cancellationToken);
            await RefreshAsync(null, cancellationToken);
        }, cancellationToken);
    }

    [RelayCommand]
    private Task MoveRuleUpAsync(MetadataRuleRow? row, CancellationToken cancellationToken) => MoveAsync(row, -1, cancellationToken);

    [RelayCommand]
    private Task MoveRuleDownAsync(MetadataRuleRow? row, CancellationToken cancellationToken) => MoveAsync(row, 1, cancellationToken);

    private async Task MoveAsync(MetadataRuleRow? row, int delta, CancellationToken cancellationToken)
    {
        if (row is null || !await ConfirmLeaveAsync(cancellationToken)) return;
        if (!RuleReorderController.TryMoveByOffset(Rules.Select(item => item.Id).ToArray(),
                row.Id, delta, out var ids, StringComparer.Ordinal)) return;
        await SaveOrderAsync(ids, cancellationToken);
    }

    [RelayCommand]
    private async Task ReorderRuleAsync(RuleReorderRequest? request, CancellationToken cancellationToken)
    {
        if (request?.Source is not MetadataRuleRow row || !await ConfirmLeaveAsync(cancellationToken)) return;
        var ids = Rules.Select(item => item.Id).ToArray();
        if (RuleReorderController.TryMoveToSlot(ids, ids, row.Id, request.SlotIndex, out var reordered))
        {
            await SaveOrderAsync(reordered, cancellationToken);
        }
    }

    private Task SaveOrderAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            try
            {
                await WriteOrderAsync(ids.Select((id, index) => (id, (index + 1) * 10)).ToArray(), cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await RefreshAsync(Original?.Id, cancellationToken);
                throw;
            }
            await RefreshAsync(Original?.Id, cancellationToken);
        }, cancellationToken);

    [RelayCommand]
    private Task ExportRuleAsync(MetadataRuleRow? row, CancellationToken cancellationToken) =>
        ExchangeRuleAsync(row, false, cancellationToken);

    [RelayCommand]
    private Task CopyRuleAsync(MetadataRuleRow? row, CancellationToken cancellationToken) =>
        ExchangeRuleAsync(row, true, cancellationToken);

    private async Task ExchangeRuleAsync(MetadataRuleRow? row, bool clipboard, CancellationToken cancellationToken)
    {
        if ((!IsManagementMode && row is null) || (IsManagementMode && SelectedCount == 0)) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _activation?.CancellationToken ?? CancellationToken.None);
        await ExecuteAsync(async () =>
        {
            var rules = RulesForExchange(row);
            var result = await _exchange.WriteAsync(_ => Task.FromResult<string?>(Serialize(rules)),
                clipboard, DocumentFileName, linked.Token);
            if (result == ExchangeWriteResult.Completed)
                _feedback.ShowSuccess(clipboard ? "复制完成" : "导出完成", $"成功 {rules.Count}，跳过 0，失败 0。");
        }, linked.Token);
    }

    private IReadOnlyList<MetadataRuleState> RulesForExchange(MetadataRuleRow? row) =>
        IsManagementMode
            ? Rules.Where(candidate => _selection.IsSelected(candidate.Id)).Select(candidate => candidate.State).ToArray()
            : row is null ? [] : [row.State];

    [RelayCommand]
    private Task ImportFileAsync(CancellationToken cancellationToken) =>
        ImportAsync(_documents.PickImportAsync, cancellationToken);

    [RelayCommand]
    private Task ImportClipboardAsync(CancellationToken cancellationToken) =>
        ImportAsync(_documents.ReadClipboardAsync, cancellationToken);

    private async Task ImportAsync(Func<CancellationToken, Task<RuleImportDocument?>> read, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _activation?.CancellationToken ?? CancellationToken.None);
        try
        {
            var execution = await _importSession.RunAsync(read, ImportJsonAsyncCore,
                () => IsBusy, value => IsBusy = value, linked.Token);
            if (execution is null) return;
            var result = execution.Result;
            _feedback.ShowSuccess("元数据规则导入完成", $"导入 {result.ImportedCount} 条，跳过重复 {result.SkippedCount} 条，失败 {result.FailedCount} 条。");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception exception) when (!linked.IsCancellationRequested)
        {
            _feedback.ShowProjectedNotification("元数据规则操作失败", _feedback.Project(exception));
        }
        catch (Exception) when (linked.IsCancellationRequested) { }
    }

    private async Task<RuleImportResult> ImportJsonAsyncCore(RuleImportDocument document, CancellationToken cancellationToken)
    {
        var imported = 0;
        var skipped = 0;
        var failed = 0;
        using var json = JsonDocument.Parse(document.Json);
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("schemaVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1 ||
            !json.RootElement.TryGetProperty("ruleType", out var ruleType) ||
            ruleType.ValueKind != JsonValueKind.String || ruleType.GetString() != DocumentRuleType ||
            !json.RootElement.TryGetProperty("rules", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("元数据规则文档版本或结构无效。");
        }

        var existing = (await ReadAsync(cancellationToken)).ToList();
        foreach (var item in entries.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("name", out var nameValue) ||
                    !item.TryGetProperty("pattern", out var patternValue))
                {
                    throw new InvalidOperationException("规则缺少名称或正则表达式。");
                }

                var name = nameValue.GetString()?.Trim() ?? string.Empty;
                var pattern = patternValue.GetString()?.Trim() ?? string.Empty;
                var enabled = item.TryGetProperty("isEnabled", out var enabledValue) ? enabledValue.GetBoolean() : true;
                if (name.Length == 0) throw new InvalidOperationException("规则名称不能为空。");
                ImportMetadataExtractor.ValidatePattern(pattern);
                if (existing.Any(rule => rule.Name == name && rule.Pattern == pattern && rule.IsEnabled == enabled))
                {
                    skipped++;
                    continue;
                }

                var uniqueName = name;
                for (var suffix = 2; existing.Any(rule => string.Equals(rule.Name, uniqueName, StringComparison.OrdinalIgnoreCase)); suffix++)
                {
                    uniqueName = $"{name}({suffix})";
                }

                var now = DateTimeOffset.UtcNow;
                var rule = new MetadataRuleState(Guid.NewGuid().ToString("N"), uniqueName, pattern,
                    existing.Count == 0 ? 10 : existing.Max(candidate => candidate.SortOrder) + 10,
                    enabled, now, now);
                await WriteAsync(rule, cancellationToken);
                existing.Add(rule);
                imported++;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
            {
                failed++;
            }
        }

        await RefreshAsync(Original?.Id, cancellationToken, preserveDraft: true);
        return new RuleImportResult(imported, skipped, imported + skipped + failed, failed);
    }

    private string Serialize(IReadOnlyList<MetadataRuleState> rules) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            ruleType = DocumentRuleType,
            rules = rules.Select(rule => new { name = rule.Name, pattern = rule.Pattern, isEnabled = rule.IsEnabled })
        });

    public Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken) =>
        _editorSession.ConfirmLeaveAsync(
            token => _dialogs.ShowUnsavedChangesAsync(
                "未保存的修改", "当前元数据规则有未保存的修改。要先保存再继续吗？",
                "保存", "放弃", "取消", token),
            async token => await SaveCoreAsync(token),
            _ => { CancelEditing(); return Task.CompletedTask; }, cancellationToken);

    private async Task RefreshAsync(string? selectedId, CancellationToken cancellationToken, bool preserveDraft = false)
    {
        var states = await ReadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Rules.Clear();
        foreach (var state in states.OrderBy(rule => rule.SortOrder).ThenBy(rule => rule.Id, StringComparer.Ordinal))
        {
            Rules.Add(new MetadataRuleRow(state));
        }
        _selection.SetItems(Rules.Select(row => row.Id));
        SyncSelection();

        if (preserveDraft) return;

        var selected = Rules.FirstOrDefault(row => row.Id == selectedId);
        if (selected is null)
        {
            CloseEditor();
        }
        else
        {
            Open(selected.State);
        }
    }

    private void Open(MetadataRuleState state, bool isNew = false)
    {
        _editorSession.Open(isNew ? null : state.Id, state, isNew, null);
        DraftName = state.Name;
        DraftPattern = state.Pattern;
        ValidationMessage = string.Empty;
        _editorSession.UpdateDirty(state);
        NotifyEditorState();
        SyncSelection();
    }

    private void CloseEditor()
    {
        _editorSession.Close();
        NotifyEditorState();
        SyncSelection();
    }

    partial void OnDraftNameChanged(string value) => UpdateDirty();
    partial void OnDraftPatternChanged(string value) => UpdateDirty();

    private void UpdateDirty()
    {
        if (_editorSession.Baseline is { } baseline)
            _editorSession.UpdateDirty(baseline with { Name = DraftName, Pattern = DraftPattern });
        NotifyEditorState();
    }

    private void NotifyEditorState()
    {
        OnPropertyChanged(nameof(HasEditor));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void SyncSelection()
    {
        foreach (var row in Rules) row.IsSelected = IsManagementMode ? _selection.IsSelected(row.Id) : row.Id == Original?.Id;
    }

    private async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsBusy = true;
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _feedback.ShowProjectedNotification("元数据规则操作失败", _feedback.Project(exception));
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            IsBusy = false;
        }
    }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanReorder));


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

    public void HandleRuleRightClick(MetadataRuleRow item) => _selection.HandleRightClick(item.Id);

    [RelayCommand]
    private async Task DeleteSelectedRulesAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || !IsManagementMode || SelectedCount == 0) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _activation?.CancellationToken ?? CancellationToken.None);
        try
        {
            var result = await _batchDelete.RunAsync<MetadataRuleRow>(
                async token =>
                {
                    if (!await ConfirmLeaveAsync(token)) return [];
                    var items = Rules.Where(item => _selection.IsSelected(item.Id)).ToArray();
                    if (items.Length == 0) return [];
                    return await _feedback.ConfirmDeletionAsync("删除规则", $"将删除所选 {items.Length} 条规则，此操作不可撤销。", token)
                        == AppConfirmationDecision.Confirm ? items : [];
                },
                async (item, token) =>
                {
                    await RemoveAsync(item.Id, token);
                    return true;
                },
                token => RefreshAsync(Original?.Id, token),
                value => IsBusy = value, linked.Token);
            if (result is null) return;
            var message = $"成功 {result.Succeeded}，跳过 {result.Skipped}，失败 {result.Failed}。";
            if (result.Skipped > 0 || result.Failed > 0) _feedback.ShowWarning("删除完成", message);
            else _feedback.ShowSuccess("删除完成", message);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception exception) when (!linked.IsCancellationRequested)
        {
            _feedback.ShowProjectedNotification("批量删除失败", _feedback.Project(exception));
        }
        catch (Exception) when (linked.IsCancellationRequested) { }
    }
}
