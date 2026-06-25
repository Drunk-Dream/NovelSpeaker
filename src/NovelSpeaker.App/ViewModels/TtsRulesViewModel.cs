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
    private readonly ITtsRuleTestService _ruleTestService;
    private TtsRuleImportPreview? _pendingPreview;
    private CancellationTokenSource? _testOperationCts;

    public TtsRulesViewModel(
        ITtsRuleLibraryService ruleLibraryService,
        ITtsRuleTestService ruleTestService)
    {
        _ruleLibraryService = ruleLibraryService;
        _ruleTestService = ruleTestService;
    }

    public ObservableCollection<TtsRuleSummary> Rules { get; } = [];

    public ObservableCollection<TtsRuleImportItem> PreviewItems { get; } = [];

    [ObservableProperty]
    private string statusMessage = "在这里管理 HTTP TTS 规则。";

    [ObservableProperty]
    private string testStatusMessage = "请选择一条规则，生成请求预览或开始试听。";

    [ObservableProperty]
    private bool isPreviewVisible;

    [ObservableProperty]
    private string previewSourceDescription = string.Empty;

    [ObservableProperty]
    private string previewStatusMessage = string.Empty;

    [ObservableProperty]
    private string currentRuleDisplayText = "当前规则：未选择规则";

    [ObservableProperty]
    private string testSpeakText = "你好，欢迎试听。";

    [ObservableProperty]
    private int testSpeakSpeed = 10;

    [ObservableProperty]
    private string loginInfoText = string.Empty;

    [ObservableProperty]
    private bool isTestBusy;

    [ObservableProperty]
    private bool hasTestPreview;

    [ObservableProperty]
    private string previewMethodText = "未生成";

    [ObservableProperty]
    private string previewUrlText = "未生成";

    [ObservableProperty]
    private string previewHeadersText = "无";

    [ObservableProperty]
    private string previewBodyText = "无";

    [ObservableProperty]
    private string previewDeclaredContentTypeText = "未声明";

    [ObservableProperty]
    private string previewWarningsText = "无";

    [ObservableProperty]
    private string lastResponseStatusText = "尚未执行试听。";

    [ObservableProperty]
    private string lastResponseDetailText = string.Empty;

    [ObservableProperty]
    private TtsRuleSummary? selectedRule;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var rules = await _ruleLibraryService.GetRulesAsync(cancellationToken);
        Rules.ReplaceWith(rules, rule => rule);

        SelectedRule = Rules.SelectByKeyOrFallback(
            SelectedRule?.Id,
            rule => rule.Id,
            SelectedRule,
            rule => rule.IsSelected);

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
        if (SelectedRule is null)
        {
            ClearTestProjection();
        }
    }

    [RelayCommand]
    private async Task GeneratePreviewAsync(CancellationToken cancellationToken)
    {
        await ExecuteTestOperationAsync(
            cancellationToken,
            "已取消当前请求预览。",
            async (input, token) =>
            {
                var result = await _ruleTestService.CreatePreviewAsync(input, token);
                return (
                    result.Preview,
                    result.Warnings,
                    result.Message,
                    result.IsSuccess ? "请求预览已更新。" : "请求预览生成失败。",
                    result.ErrorKind is null ? string.Empty : $"错误类型：{result.ErrorKind}");
            });
    }

    [RelayCommand]
    private async Task TestSelectedRuleAsync(CancellationToken cancellationToken)
    {
        await ExecuteTestOperationAsync(
            cancellationToken,
            "已取消当前试听请求。",
            async (input, token) =>
            {
                var result = await _ruleTestService.TestAsync(input, token);
                return (
                    result.Preview,
                    result.Warnings,
                    result.Message,
                    BuildResponseStatusText(result),
                    BuildResponseDetailText(result));
            },
            "试听已取消。");
    }

    [RelayCommand]
    private void CancelTest()
    {
        _testOperationCts?.Cancel();
        TestStatusMessage = "正在取消当前请求。";
    }

    [RelayCommand]
    private async Task ClearRuleCookiesAsync(CancellationToken cancellationToken)
    {
        if (SelectedRule is null)
        {
            TestStatusMessage = "请先选择一条规则，再清除该规则的 Cookie。";
            return;
        }

        await _ruleTestService.ClearRuleCookiesAsync(SelectedRule.Id, cancellationToken);
        TestStatusMessage = $"已清除规则 Cookie：{SelectedRule.Name}";
        LastResponseStatusText = "该规则的会话 Cookie 已清空。";
        LastResponseDetailText = string.Empty;
    }

    private void ApplyPreview(TtsRuleImportPreview preview)
    {
        _pendingPreview = preview;
        PreviewItems.ReplaceWith(preview.Items, item => item);

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

    partial void OnSelectedRuleChanged(TtsRuleSummary? value)
    {
        ClearTestProjection();
        if (value is null)
        {
            TestStatusMessage = "请选择一条规则，生成请求预览或开始试听。";
        }
        else
        {
            TestStatusMessage = $"当前已选择规则：{value.Name}";
        }
    }

    private bool TryCreateTestInput(out TtsRuleTestInput input)
    {
        input = default!;
        if (SelectedRule is null)
        {
            TestStatusMessage = "请先选择一条规则。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(TestSpeakText))
        {
            TestStatusMessage = "试听文本不能为空。";
            return false;
        }

        if (!TryParseLoginInfo(out var loginInfo, out var errorMessage))
        {
            TestStatusMessage = errorMessage;
            return false;
        }

        input = new TtsRuleTestInput(
            SelectedRule.Id,
            TestSpeakText,
            TestSpeakSpeed,
            loginInfo);
        return true;
    }

    private bool TryParseLoginInfo(
        out IReadOnlyDictionary<string, string> loginInfo,
        out string errorMessage)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in LoginInfoText.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                loginInfo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                errorMessage = "临时登录信息需按每行 key=value 的格式输入。";
                return false;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            parsed[key] = value;
        }

        loginInfo = parsed;
        errorMessage = string.Empty;
        return true;
    }

    private CancellationTokenSource BeginTestOperation(CancellationToken cancellationToken)
    {
        _testOperationCts?.Cancel();
        _testOperationCts?.Dispose();
        _testOperationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsTestBusy = true;
        return _testOperationCts;
    }

    private async Task ExecuteTestOperationAsync(
        CancellationToken cancellationToken,
        string canceledStatusMessage,
        Func<TtsRuleTestInput, CancellationToken, Task<(
            TtsRequestPreview? Preview,
            IReadOnlyList<string> Warnings,
            string StatusMessage,
            string ResponseStatusText,
            string ResponseDetailText)>> operation,
        string? canceledResponseStatusText = null)
    {
        if (!TryCreateTestInput(out var input))
        {
            return;
        }

        using var linkedCts = BeginTestOperation(cancellationToken);
        try
        {
            var result = await operation(input, linkedCts.Token);
            ApplyTestPreview(result.Preview, result.Warnings);
            TestStatusMessage = result.StatusMessage;
            LastResponseStatusText = result.ResponseStatusText;
            LastResponseDetailText = result.ResponseDetailText;
        }
        catch (OperationCanceledException)
        {
            TestStatusMessage = canceledStatusMessage;
            if (canceledResponseStatusText is not null)
            {
                LastResponseStatusText = canceledResponseStatusText;
            }
        }
        finally
        {
            EndTestOperation(linkedCts);
        }
    }

    private void EndTestOperation(CancellationTokenSource linkedCts)
    {
        if (ReferenceEquals(_testOperationCts, linkedCts))
        {
            _testOperationCts = null;
        }

        IsTestBusy = false;
    }

    private void ApplyTestPreview(TtsRequestPreview? preview, IReadOnlyList<string> warnings)
    {
        HasTestPreview = preview is not null;
        PreviewMethodText = preview?.Method ?? "未生成";
        PreviewUrlText = preview?.Url ?? "未生成";
        PreviewHeadersText = string.IsNullOrWhiteSpace(preview?.HeadersJson) ? "无" : preview.HeadersJson;
        PreviewBodyText = string.IsNullOrWhiteSpace(preview?.BodyPreview) ? "无" : preview.BodyPreview;
        PreviewDeclaredContentTypeText = string.IsNullOrWhiteSpace(preview?.DeclaredContentType) ? "未声明" : preview.DeclaredContentType;
        PreviewWarningsText = warnings.Count == 0 ? "无" : string.Join(Environment.NewLine, warnings);
    }

    private void ClearTestProjection()
    {
        HasTestPreview = false;
        PreviewMethodText = "未生成";
        PreviewUrlText = "未生成";
        PreviewHeadersText = "无";
        PreviewBodyText = "无";
        PreviewDeclaredContentTypeText = "未声明";
        PreviewWarningsText = "无";
        LastResponseStatusText = "尚未执行试听。";
        LastResponseDetailText = string.Empty;
    }

    private static string BuildResponseStatusText(TtsRuleTestResult result)
    {
        if (result.IsSuccess)
        {
            return $"试听成功，HTTP {result.StatusCode}";
        }

        if (result.StatusCode is null)
        {
            return result.ErrorKind is null ? "试听失败。" : $"试听失败：{result.ErrorKind}";
        }

        return result.ErrorKind is null
            ? $"试听失败，HTTP {result.StatusCode}"
            : $"试听失败：{result.ErrorKind} / HTTP {result.StatusCode}";
    }

    private static string BuildResponseDetailText(TtsRuleTestResult result)
    {
        var details = new List<string>();

        if (!string.IsNullOrWhiteSpace(result.ResponseContentType))
        {
            details.Add($"Content-Type：{result.ResponseContentType}");
        }

        if (result.RetryAfter is not null)
        {
            details.Add($"Retry-After：{result.RetryAfter.Value.TotalSeconds:0.#} 秒");
        }

        if (!string.IsNullOrWhiteSpace(result.ResponseSummary))
        {
            details.Add(result.ResponseSummary);
        }

        return details.Count == 0 ? string.Empty : string.Join(Environment.NewLine, details);
    }
}
