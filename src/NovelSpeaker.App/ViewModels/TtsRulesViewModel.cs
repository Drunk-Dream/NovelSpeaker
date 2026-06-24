using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Drives the HTTP TTS rules page, including import preview and current-rule selection.
/// </summary>
public sealed partial class TtsRulesViewModel : ObservableObject
{
    private readonly ITtsRuleLibraryService _ruleLibraryService;
    private TtsRuleImportPreview? _pendingPreview;

    public TtsRulesViewModel(ITtsRuleLibraryService ruleLibraryService)
    {
        _ruleLibraryService = ruleLibraryService;
    }

    public ObservableCollection<TtsRuleSummary> Rules { get; } = [];

    public ObservableCollection<TtsRuleImportItem> PreviewItems { get; } = [];

    [ObservableProperty]
    private string statusMessage = "在这里管理 HTTP TTS 规则。";

    [ObservableProperty]
    private bool isPreviewVisible;

    [ObservableProperty]
    private string previewSourceDescription = string.Empty;

    [ObservableProperty]
    private string previewStatusMessage = string.Empty;

    [ObservableProperty]
    private string currentRuleDisplayText = "当前规则：未选择规则";

    [ObservableProperty]
    private TtsRuleSummary? selectedRule;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var rules = await _ruleLibraryService.GetRulesAsync(cancellationToken);
        Rules.Clear();
        foreach (var rule in rules)
        {
            Rules.Add(rule);
        }

        SelectedRule = SelectedRule is null
            ? Rules.FirstOrDefault(rule => rule.IsSelected)
            : Rules.FirstOrDefault(rule => rule.Id == SelectedRule.Id) ?? Rules.FirstOrDefault(rule => rule.IsSelected);

        CurrentRuleDisplayText = Rules.FirstOrDefault(rule => rule.IsSelected) is { } selected
            ? $"当前规则：{selected.Name}"
            : "当前规则：未选择规则";
    }

    public async Task ImportFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var jsonText = await File.ReadAllTextAsync(filePath, cancellationToken);
        await ImportJsonTextAsync(jsonText, Path.GetFileName(filePath), cancellationToken);
    }

    public async Task ImportJsonTextAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken)
    {
        var preview = await _ruleLibraryService.CreateImportPreviewAsync(jsonText, sourceDescription, cancellationToken);
        ApplyPreview(preview);
    }

    public async Task ExportSelectedRuleToFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (SelectedRule is null)
        {
            StatusMessage = "请先选择一条规则再导出。";
            return;
        }

        var json = await _ruleLibraryService.ExportRuleJsonAsync(SelectedRule.Id, cancellationToken);
        if (json is null)
        {
            StatusMessage = "未找到要导出的规则。";
            return;
        }

        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        StatusMessage = $"已导出规则：{SelectedRule.Name}";
    }

    [RelayCommand]
    private async Task ConfirmImportAsync(CancellationToken cancellationToken)
    {
        if (_pendingPreview is null)
        {
            StatusMessage = "当前没有待确认的规则导入。";
            return;
        }

        var result = await _ruleLibraryService.ImportAsync(_pendingPreview, cancellationToken);
        await LoadAsync(cancellationToken);
        ClearPreview();
        StatusMessage = $"导入完成：新增 {result.ImportedCount} 条，跳过 {result.SkippedCount} 条。";
    }

    [RelayCommand]
    private void CancelPreview()
    {
        ClearPreview();
        StatusMessage = "已取消本次规则导入预览。";
    }

    [RelayCommand]
    private async Task SetCurrentRuleAsync(TtsRuleSummary? rule, CancellationToken cancellationToken)
    {
        if (rule is null)
        {
            return;
        }

        if (!rule.IsEnabled)
        {
            StatusMessage = "请先启用规则，再将其设为当前规则。";
            return;
        }

        await _ruleLibraryService.SelectRuleAsync(rule.Id, cancellationToken);
        await LoadAsync(cancellationToken);
        StatusMessage = $"当前规则已切换为：{rule.Name}";
    }

    [RelayCommand]
    private async Task ToggleRuleEnabledAsync(TtsRuleSummary? rule, CancellationToken cancellationToken)
    {
        if (rule is null)
        {
            return;
        }

        await _ruleLibraryService.SetRuleEnabledAsync(rule.Id, !rule.IsEnabled, cancellationToken);
        await LoadAsync(cancellationToken);
        StatusMessage = rule.IsEnabled
            ? $"已禁用规则：{rule.Name}"
            : $"已启用规则：{rule.Name}";
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(TtsRuleSummary? rule, CancellationToken cancellationToken)
    {
        if (rule is null)
        {
            return;
        }

        await _ruleLibraryService.DeleteRuleAsync(rule.Id, cancellationToken);
        await LoadAsync(cancellationToken);
        StatusMessage = $"已删除规则：{rule.Name}";
    }

    private void ApplyPreview(TtsRuleImportPreview preview)
    {
        _pendingPreview = preview;
        PreviewItems.Clear();
        foreach (var item in preview.Items)
        {
            PreviewItems.Add(item);
        }

        PreviewSourceDescription = preview.SourceDescription;
        IsPreviewVisible = true;
        PreviewStatusMessage = preview.ErrorMessage ??
            $"共解析 {preview.Items.Count} 条规则，可导入 {preview.ImportableCount} 条，跳过 {preview.SkippedCount} 条。";
        StatusMessage = preview.ErrorMessage ?? "请确认本次规则导入。";
    }

    private void ClearPreview()
    {
        _pendingPreview = null;
        PreviewItems.Clear();
        PreviewSourceDescription = string.Empty;
        PreviewStatusMessage = string.Empty;
        IsPreviewVisible = false;
    }
}
