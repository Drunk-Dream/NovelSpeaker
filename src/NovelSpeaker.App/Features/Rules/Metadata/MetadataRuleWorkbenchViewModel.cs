using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books.Import;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Features.Rules.Shared;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.Rules.Metadata;

/// <summary>Shared visual editing behavior for the two independent metadata rule stores.</summary>
public abstract partial class MetadataRuleWorkbenchViewModel : ObservableObject
{
    private readonly IAppNavigator _navigator;
    private readonly IAppDialogService _dialogs;
    private readonly IAppFeedbackService _feedback;
    private readonly IRuleDocumentInteraction _documents;
    private readonly DesktopSelectionController<string> _selection = new(StringComparer.Ordinal);
    private MetadataRuleState? _original;
    private bool _isNew;

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
        _selection.SelectionChanged += (_, _) => SyncSelection();
    }

    public abstract string PageTitle { get; }
    protected abstract string DocumentFileName { get; }
    protected abstract string DocumentRuleType { get; }
    protected abstract Task<IReadOnlyList<MetadataRuleState>> ReadAsync(CancellationToken cancellationToken);
    protected abstract Task WriteAsync(MetadataRuleState rule, CancellationToken cancellationToken);
    protected abstract Task RemoveAsync(string id, CancellationToken cancellationToken);
    protected abstract Task WriteOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken);

    public ObservableCollection<MetadataRuleRow> Rules { get; } = [];

    [ObservableProperty] private bool hasEditor;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string draftName = string.Empty;
    [ObservableProperty] private string draftPattern = string.Empty;
    [ObservableProperty] private string validationMessage = string.Empty;

    public bool HasUnsavedChanges => HasEditor &&
        (_isNew || !string.Equals(DraftName, _original?.Name, StringComparison.Ordinal) ||
         !string.Equals(DraftPattern, _original?.Pattern, StringComparison.Ordinal));

    public async Task LoadAsync(CancellationToken cancellationToken) => await RefreshAsync(null, cancellationToken);

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
        _original = null;
        _isNew = true;
        HasEditor = true;
        DraftName = string.Empty;
        DraftPattern = string.Empty;
        ValidationMessage = string.Empty;
        MarkSelected(null);
    }

    [RelayCommand]
    private async Task SelectRuleAsync(MetadataRuleRow? row, CancellationToken cancellationToken)
    {
        if (row is null || IsBusy) return;
        if (!_isNew && _original?.Id == row.Id)
        {
            MarkSelected(row.Id);
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
        if (modifiers == DesktopSelectionModifiers.None)
        {
            await SelectRuleAsync(row, cancellationToken);
            return;
        }

        _selection.Click(row.Id, modifiers);
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

        if (Rules.Any(row => row.Id != _original?.Id && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationMessage = "规则名称已存在。";
            return false;
        }

        try
        {
            IsBusy = true;
            var now = DateTimeOffset.UtcNow;
            var isNewRule = _isNew;
            var rule = new MetadataRuleState(
                _original?.Id ?? Guid.NewGuid().ToString("N"), name, pattern,
                _original?.SortOrder ?? (Rules.Count == 0 ? 10 : Rules.Max(row => row.State.SortOrder) + 10),
                _original?.IsEnabled ?? true, _original?.CreatedAt ?? now, now);
            await WriteAsync(rule, cancellationToken);
            await RefreshAsync(rule.Id, cancellationToken);
            if (isNewRule) MarkSelected(rule.Id);
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
        if (_isNew)
        {
            HasEditor = false;
            _isNew = false;
            _original = null;
            MarkSelected(null);
        }
        else if (_original is not null)
        {
            Open(_original);
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
            await RefreshAsync(_original?.Id, cancellationToken);
        });
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(MetadataRuleRow? row, CancellationToken cancellationToken)
    {
        if (row is null || !await ConfirmLeaveAsync(cancellationToken)) return;
        if (await _feedback.ConfirmDeletionAsync("删除元数据规则", $"确定删除“{row.Name}”吗？", cancellationToken) != AppConfirmationDecision.Confirm) return;
        await ExecuteAsync(async () =>
        {
            await RemoveAsync(row.Id, cancellationToken);
            await RefreshAsync(null, cancellationToken);
        });
    }

    [RelayCommand]
    private Task MoveRuleUpAsync(MetadataRuleRow? row, CancellationToken cancellationToken) => MoveAsync(row, -1, cancellationToken);

    [RelayCommand]
    private Task MoveRuleDownAsync(MetadataRuleRow? row, CancellationToken cancellationToken) => MoveAsync(row, 1, cancellationToken);

    private async Task MoveAsync(MetadataRuleRow? row, int delta, CancellationToken cancellationToken)
    {
        if (row is null || !await ConfirmLeaveAsync(cancellationToken)) return;
        var index = Rules.ToList().FindIndex(candidate => candidate.Id == row.Id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Rules.Count) return;
        var ids = Rules.Select(item => item.Id).ToList();
        (ids[index], ids[target]) = (ids[target], ids[index]);
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
            await WriteOrderAsync(ids.Select((id, index) => (id, (index + 1) * 10)).ToArray(), cancellationToken);
            await RefreshAsync(_original?.Id, cancellationToken);
        });

    [RelayCommand]
    private Task ExportRuleAsync(MetadataRuleRow? row, CancellationToken cancellationToken) =>
        row is null ? Task.CompletedTask : ExecuteAsync(async () =>
            await _documents.ExportAsync(DocumentFileName, Serialize(RulesForExchange(row)), cancellationToken));

    [RelayCommand]
    private Task CopyRuleAsync(MetadataRuleRow? row, CancellationToken cancellationToken) =>
        row is null ? Task.CompletedTask : ExecuteAsync(() => _documents.CopyAsync(Serialize(RulesForExchange(row)), cancellationToken));

    private IReadOnlyList<MetadataRuleState> RulesForExchange(MetadataRuleRow row) =>
        _selection.IsSelected(row.Id) && _selection.Count > 1
            ? Rules.Where(candidate => _selection.IsSelected(candidate.Id)).Select(candidate => candidate.State).ToArray()
            : [row.State];

    [RelayCommand]
    private Task ImportFileAsync(CancellationToken cancellationToken) =>
        ImportAsync(() => _documents.PickImportAsync(cancellationToken), cancellationToken);

    [RelayCommand]
    private Task ImportClipboardAsync(CancellationToken cancellationToken) =>
        ImportAsync(() => _documents.ReadClipboardAsync(cancellationToken), cancellationToken);

    private async Task ImportAsync(Func<Task<RuleImportDocument?>> read, CancellationToken cancellationToken)
    {
        await ExecuteAsync(async () =>
        {
            var document = await read();
            if (document is null) return;
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

            await RefreshAsync(_original?.Id, cancellationToken, preserveDraft: true);
            _feedback.ShowSuccess("元数据规则导入完成", $"导入 {imported} 条，跳过重复 {skipped} 条，失败 {failed} 条。");
        });
    }

    private string Serialize(IReadOnlyList<MetadataRuleState> rules) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            ruleType = DocumentRuleType,
            rules = rules.Select(rule => new { name = rule.Name, pattern = rule.Pattern, isEnabled = rule.IsEnabled })
        });

    public async Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken)
    {
        if (!HasUnsavedChanges) return true;
        var decision = await _dialogs.ShowUnsavedChangesAsync(
            "未保存的修改", "当前元数据规则有未保存的修改。要先保存再继续吗？",
            "保存", "放弃", "取消", cancellationToken);
        return decision switch
        {
            UnsavedChangesDecision.Save => await SaveCoreAsync(cancellationToken),
            UnsavedChangesDecision.Discard => true,
            _ => false
        };
    }

    private async Task RefreshAsync(string? selectedId, CancellationToken cancellationToken, bool preserveDraft = false)
    {
        var states = await ReadAsync(cancellationToken);
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
            _original = null;
            _isNew = false;
            HasEditor = false;
            MarkSelected(null);
        }
        else
        {
            Open(selected.State, resetSelection: false);
        }
    }

    private void Open(MetadataRuleState state, bool resetSelection = true)
    {
        _original = state;
        _isNew = false;
        HasEditor = true;
        DraftName = state.Name;
        DraftPattern = state.Pattern;
        ValidationMessage = string.Empty;
        if (resetSelection) MarkSelected(state.Id);
    }

    private void MarkSelected(string? id)
    {
        if (id is null) _selection.Clear();
        else _selection.Click(id);
        SyncSelection();
    }

    private void SyncSelection()
    {
        foreach (var row in Rules) row.IsSelected = _selection.IsSelected(row.Id);
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _feedback.ShowProjectedNotification("元数据规则操作失败", _feedback.Project(exception));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
