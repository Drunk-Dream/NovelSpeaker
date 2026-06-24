using System.Text.Json;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

/// <summary>
/// Handles import, selection, export, and projection of persisted HTTP TTS rules.
/// </summary>
public sealed class TtsRuleLibraryService : ITtsRuleLibraryService
{
    private readonly ITtsRuleConverter _ruleConverter;
    public async Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await _repository.GetAllAsync(cancellationToken);
        var settings = await _settingsStore.LoadAsync(cancellationToken);

        return rules
            .Select(rule => new TtsRuleSummary(
                rule.Id,
                rule.Name,
                rule.IsEnabled,
                settings.SelectedTtsRuleId == rule.Id && rule.IsEnabled,
                rule.LastUsedAt,
                rule.CompatibilityStatus,
                rule.UnsupportedFields))
            .ToArray();
    }

    public async Task<TtsRuleImportPreview> CreateImportPreviewAsync(
        string jsonText,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return new TtsRuleImportPreview(sourceDescription, [], "没有可导入的 JSON 内容。");
        }

        try
        {
            using var document = JsonDocument.Parse(jsonText);
            var root = document.RootElement;
            if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                return new TtsRuleImportPreview(sourceDescription, [], "规则 JSON 必须是对象或对象数组。");
            }

            var existingRules = await _repository.GetAllAsync(cancellationToken);
            var items = new List<TtsRuleImportItem>();
            var index = 0;

            if (root.ValueKind == JsonValueKind.Object)
            {
                items.Add(CreateImportItem(root, index, existingRules));
            }
            else
            {
                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        items.Add(CreateInvalidItem(index, "规则数组中的每一项都必须是对象。"));
                        index++;
                        continue;
                    }

                    items.Add(CreateImportItem(element, index, existingRules));
                    index++;
                }
            }

            return new TtsRuleImportPreview(sourceDescription, items, null);
        }
        catch (JsonException)
        {
            return new TtsRuleImportPreview(sourceDescription, [], "JSON 解析失败，请检查规则内容。");
        }
    }

    public async Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken)
    {
        if (preview.ErrorMessage is not null)
        {
            return new TtsRuleImportResult(0, preview.Items.Count, preview.Items.Count);
        }

        var importedCount = 0;
        var skippedCount = 0;
        var existingRules = (await _repository.GetAllAsync(cancellationToken)).ToList();

        foreach (var item in preview.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!item.CanImport || existingRules.Any(rule => string.Equals(rule.RuleJson, item.CandidateRule.RuleJson, StringComparison.Ordinal)))
            {
                skippedCount++;
                continue;
            }

            var utcNow = DateTime.UtcNow.ToString("O");
            await _repository.SaveAsync(item.CandidateRule with
            {
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            }, cancellationToken);
            existingRules.Add(item.CandidateRule);
            importedCount++;
        }

        return new TtsRuleImportResult(importedCount, skippedCount, preview.Items.Count);
    }

    public async Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(ruleId, cancellationToken);
        return rule?.RuleJson;
    }

    public async Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken)
    {
        if (ruleId is null)
        {
            var settings = await _settingsStore.LoadAsync(cancellationToken);
            await _settingsStore.SaveAsync(settings with { SelectedTtsRuleId = null }, cancellationToken);
            return;
        }

        var rule = await _repository.GetByIdAsync(ruleId.Value, cancellationToken);
        if (rule is null || !rule.IsEnabled)
        {
            throw new InvalidOperationException("只能将存在且已启用的规则设为当前规则。");
        }

        var utcNow = DateTime.UtcNow.ToString("O");
        await _repository.SaveAsync(rule with { LastUsedAt = utcNow, UpdatedAt = utcNow }, cancellationToken);

        var currentSettings = await _settingsStore.LoadAsync(cancellationToken);
        await _settingsStore.SaveAsync(currentSettings with { SelectedTtsRuleId = rule.Id }, cancellationToken);
    }

    public async Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(ruleId, cancellationToken);
        if (rule is null)
        {
            return;
        }

        var utcNow = DateTime.UtcNow.ToString("O");
        await _repository.SaveAsync(rule with
        {
            IsEnabled = isEnabled,
            UpdatedAt = utcNow
        }, cancellationToken);

        if (isEnabled)
        {
            return;
        }

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        if (settings.SelectedTtsRuleId == ruleId)
        {
            await _settingsStore.SaveAsync(settings with { SelectedTtsRuleId = null }, cancellationToken);
        }
    }

    public async Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken)
    {
        await _repository.DeleteAsync(ruleId, cancellationToken);

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        if (settings.SelectedTtsRuleId == ruleId)
        {
            await _settingsStore.SaveAsync(settings with { SelectedTtsRuleId = null }, cancellationToken);
        }
    }

    private readonly ITtsRuleRepository _repository;
    private readonly IAppSettingsStore _settingsStore;

    public TtsRuleLibraryService(
        ITtsRuleRepository repository,
        IAppSettingsStore settingsStore,
        ITtsRuleConverter ruleConverter)
    {
        _repository = repository;
        _settingsStore = settingsStore;
        _ruleConverter = ruleConverter;
    }

    private TtsRuleImportItem CreateImportItem(JsonElement element, int index, IReadOnlyList<HttpTtsRule> existingRules)
    {
        var conversion = _ruleConverter.Convert(element);
        var candidateRule = conversion.CandidateRule;
        var ruleJson = candidateRule.RuleJson;
        var exactDuplicate = existingRules.Any(rule => string.Equals(rule.RuleJson, ruleJson, StringComparison.Ordinal));
        var sameNameConflict = !string.IsNullOrWhiteSpace(candidateRule.Name) &&
                               existingRules.Any(rule => string.Equals(rule.Name, candidateRule.Name, StringComparison.OrdinalIgnoreCase)) &&
                               !exactDuplicate;
        var canImport = conversion.CanImport && !exactDuplicate;
        var statusMessage = BuildStatusMessage(conversion, exactDuplicate, sameNameConflict);

        return new TtsRuleImportItem(
            index,
            string.IsNullOrWhiteSpace(candidateRule.Name) ? $"未命名规则 #{index + 1}" : candidateRule.Name,
            candidateRule.Url,
            conversion.CompatibilityStatus,
            conversion.UnsupportedFields,
            canImport,
            exactDuplicate,
            sameNameConflict,
            statusMessage,
            candidateRule with
            {
                CompatibilityStatus = conversion.CompatibilityStatus,
                UnsupportedFields = conversion.UnsupportedFields
            });
    }

    private static TtsRuleImportItem CreateInvalidItem(int index, string statusMessage)
    {
        var utcNow = DateTime.UtcNow.ToString("O");

        return new TtsRuleImportItem(
            index,
            $"无效规则 #{index + 1}",
            string.Empty,
            TtsRuleCompatibilityStatus.NeedsManualAdjustment,
            [],
            false,
            false,
            false,
            statusMessage,
            new HttpTtsRule(
                0,
                string.Empty,
                string.Empty,
                null,
                null,
                null,
                null,
                false,
                null,
                "{}",
                false,
                TtsRuleCompatibilityStatus.NeedsManualAdjustment,
                [],
                null,
                utcNow,
                utcNow));
    }

    private static string BuildStatusMessage(
        TtsRuleConversionResult conversion,
        bool exactDuplicate,
        bool sameNameConflict)
    {
        if (exactDuplicate)
        {
            return "与现有规则完全相同，将跳过导入。";
        }

        if (sameNameConflict)
        {
            return "名称与现有规则重复，但内容不同，将作为新规则导入。";
        }

        if (conversion.BlockingIssues.Count > 0)
        {
            return string.Join(" ", conversion.BlockingIssues);
        }

        return conversion.CompatibilityStatus switch
        {
            TtsRuleCompatibilityStatus.Compatible => "可直接导入。",
            TtsRuleCompatibilityStatus.CompatibleWithWarnings => $"可导入，但包含未支持字段：{string.Join("、", conversion.UnsupportedFields)}。",
            _ => "当前规则无法转换为本应用规则。"
        };
    }
}
