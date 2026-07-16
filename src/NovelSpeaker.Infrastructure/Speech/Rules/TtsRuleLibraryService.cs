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
    private readonly ITtsRuleRepository _repository;
    private readonly IAppSettingsService _settingsService;
    private readonly ITtsRuleConverter _ruleConverter;

    public TtsRuleLibraryService(
        ITtsRuleRepository repository,
        IAppSettingsService settingsService,
        ITtsRuleConverter ruleConverter)
    {
        _repository = repository;
        _settingsService = settingsService;
        _ruleConverter = ruleConverter;
    }

    public async Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await _repository.GetAllAsync(cancellationToken);
        var settings = await _settingsService.LoadAsync(cancellationToken);

        return rules.Select(rule => TtsRuleModelMapper.ToSummary(rule, settings.SelectedTtsRuleId)).ToArray();
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
            return new TtsRuleImportPreview(sourceDescription, CreateImportItems(root, existingRules), null);
        }
        catch (JsonException)
        {
            return new TtsRuleImportPreview(sourceDescription, [], "JSON 解析失败，请检查规则内容。");
        }
    }

    public async Task<TtsRuleImportResult> ImportJsonTextAsync(
        string jsonText,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        var preview = await CreateImportPreviewAsync(jsonText, sourceDescription, cancellationToken);
        return await ImportAsync(preview, cancellationToken);
    }

    public async Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken)
    {
        if (preview.ErrorMessage is not null)
        {
            return new TtsRuleImportResult(0, 0, preview.Items.Count)
            {
                FailedCount = preview.Items.Count
            };
        }

        var importedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        long? firstImportedRuleId = null;
        long? firstImportedEnabledRuleId = null;
        var existingRules = (await _repository.GetAllAsync(cancellationToken)).ToList();

        foreach (var item in preview.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsExactDuplicate(existingRules, item))
            {
                skippedCount++;
                continue;
            }

            if (!item.CanImport)
            {
                failedCount++;
                continue;
            }

            var savedRule = await SaveImportedRuleAsync(item.CandidateRule, existingRules, cancellationToken);
            firstImportedRuleId ??= savedRule.Id;
            if (savedRule.IsEnabled)
            {
                firstImportedEnabledRuleId ??= savedRule.Id;
            }

            existingRules.Add(savedRule);
            importedCount++;
        }

        if (firstImportedEnabledRuleId is not null)
        {
            var settings = await _settingsService.LoadAsync(cancellationToken);
            if (settings.SelectedTtsRuleId is null)
            {
                await SelectRuleAsync(firstImportedEnabledRuleId.Value, cancellationToken);
            }
        }

        return new TtsRuleImportResult(importedCount, skippedCount, preview.Items.Count)
        {
            FailedCount = failedCount,
            FirstImportedRuleId = firstImportedRuleId
        };
    }

    public async Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(ruleId, cancellationToken);
        return rule is null ? null : TtsRuleModelMapper.ExportRuleJson(rule);
    }

    public async Task<string> ExportEditorJsonAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
    {
        var validation = await ValidateEditorAsync(editor, cancellationToken);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        }

        var existingRule = validation.NormalizedModel.Id is > 0
            ? await _repository.GetByIdAsync(validation.NormalizedModel.Id.Value, cancellationToken)
            : null;
        var rule = TtsRuleModelMapper.BuildRuleFromEditor(validation.NormalizedModel, existingRule);
        return TtsRuleModelMapper.ExportRuleJson(rule);
    }

    public async Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(ruleId, cancellationToken);
        return rule is null ? null : TtsRuleModelMapper.ToEditor(rule);
    }

    public async Task<TtsRuleValidationResult> ValidateEditorAsync(
        TtsRuleEditorModel editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (editor.Id is > 0)
        {
            _ = await _repository.GetByIdAsync(editor.Id.Value, cancellationToken);
        }

        var normalizedEditor = TtsRuleModelMapper.NormalizeEditor(editor);
        var errors = TtsRuleModelMapper.Validate(normalizedEditor).ToList();
        if (TtsRuleCompatibilityChecker.HasUnsupportedEditorDependency(normalizedEditor))
        {
            errors.Add(TtsRuleCompatibilityChecker.UnsupportedCookieLoginInfoMessage);
        }

        return new TtsRuleValidationResult(errors.Count == 0, errors, normalizedEditor);
    }

    public async Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
    {
        var validation = await ValidateEditorAsync(editor, cancellationToken);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        }

        var existingRule = validation.NormalizedModel.Id is > 0
            ? await _repository.GetByIdAsync(validation.NormalizedModel.Id.Value, cancellationToken)
            : null;
        var rule = TtsRuleModelMapper.BuildRuleFromEditor(validation.NormalizedModel, existingRule);
        var existingRules = await _repository.GetAllAsync(cancellationToken);
        rule = EnsureUniqueRuleName(rule, existingRules, existingRule?.Id);

        var ruleId = await _repository.SaveAsync(rule, cancellationToken);
        var savedRule = (await _repository.GetByIdAsync(ruleId, cancellationToken))!;

        if (existingRule is null && savedRule.IsEnabled)
        {
            var settings = await _settingsService.LoadAsync(cancellationToken);
            if (settings.SelectedTtsRuleId is null)
            {
                await SelectRuleAsync(savedRule.Id, cancellationToken);
                savedRule = (await _repository.GetByIdAsync(ruleId, cancellationToken))!;
            }
        }

        return savedRule;
    }

    public async Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(
        long ruleId,
        TtsRuleMutationAction action,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
        var isCurrentRule = settings.SelectedTtsRuleId == ruleId;
        var rules = await GetRulesAsync(cancellationToken);
        var replacementCandidates = rules
            .Where(rule => rule.Id != ruleId && rule.IsEnabled)
            .ToArray();

        return new TtsRuleProtectionInfo(
            ruleId,
            action,
            isCurrentRule,
            !isCurrentRule,
            isCurrentRule,
            replacementCandidates);
    }

    public async Task<TtsRuleMutationResult> ApplyRuleMutationAsync(
        TtsRuleMutationDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var protection = await GetRuleProtectionAsync(decision.RuleId, decision.Action, cancellationToken);
        if (protection.IsCurrentRule && !protection.CanApplyDirectly)
        {
            if (decision.ReplacementRuleId is not null)
            {
                var replacementRule = await _repository.GetByIdAsync(decision.ReplacementRuleId.Value, cancellationToken);
                if (replacementRule is null || !replacementRule.IsEnabled || replacementRule.Id == decision.RuleId)
                {
                    throw new InvalidOperationException("必须选择另一条已启用规则作为替代规则。");
                }
            }
            else if (!decision.ClearSelectedRule || !protection.CanClearSelectedRule)
            {
                throw new InvalidOperationException("当前规则需要明确清空当前规则后才能继续。");
            }
        }

        switch (decision.Action)
        {
            case TtsRuleMutationAction.Disable:
                {
                    var rule = await _repository.GetByIdAsync(decision.RuleId, cancellationToken);
                    if (rule is null)
                    {
                        throw new InvalidOperationException("未找到要禁用的规则。");
                    }

                    var utcNow = DateTime.UtcNow.ToString("O");
                    await _repository.SaveAsync(rule with
                    {
                        IsEnabled = false,
                        UpdatedAt = utcNow
                    }, cancellationToken);
                    break;
                }
            case TtsRuleMutationAction.Delete:
                await _repository.DeleteAsync(decision.RuleId, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision.Action, null);
        }

        if (decision.ReplacementRuleId is not null)
        {
            await SelectRuleAsync(decision.ReplacementRuleId, cancellationToken);
            return new TtsRuleMutationResult(
                decision.RuleId,
                decision.Action,
                decision.ReplacementRuleId,
                false);
        }

        if (protection.IsCurrentRule)
        {
            await UpdateSelectedRuleAsync(null, cancellationToken);
        }

        var settings = await _settingsService.LoadAsync(cancellationToken);
        return new TtsRuleMutationResult(
            decision.RuleId,
            decision.Action,
            settings.SelectedTtsRuleId,
            protection.IsCurrentRule && settings.SelectedTtsRuleId is null);
    }

    public async Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken)
    {
        if (ruleId is null)
        {
            await UpdateSelectedRuleAsync(null, cancellationToken);
            return;
        }

        var rule = await _repository.GetByIdAsync(ruleId.Value, cancellationToken);
        if (rule is null || !rule.IsEnabled)
        {
            throw new InvalidOperationException("只能将存在且已启用的规则设为当前规则。");
        }

        var utcNow = DateTime.UtcNow.ToString("O");
        await _repository.SaveAsync(rule with { LastUsedAt = utcNow, UpdatedAt = utcNow }, cancellationToken);

        await UpdateSelectedRuleAsync(rule.Id, cancellationToken);
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

        if (!isEnabled)
        {
            await ClearSelectedRuleIfNeededAsync(ruleId, cancellationToken);
        }
    }

    public async Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken)
    {
        await _repository.DeleteAsync(ruleId, cancellationToken);
        await ClearSelectedRuleIfNeededAsync(ruleId, cancellationToken);
    }

    private TtsRuleImportItem CreateImportItem(JsonElement element, int index, IReadOnlyList<HttpTtsRule> existingRules)
    {
        var conversion = _ruleConverter.Convert(element);
        var candidateRule = conversion.CandidateRule;
        var ruleJson = TtsRuleModelMapper.ExportRuleJson(candidateRule);
        var exactDuplicate = existingRules.Any(rule =>
            string.Equals(TtsRuleModelMapper.ExportRuleJson(rule), ruleJson, StringComparison.Ordinal));
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
            candidateRule);
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
                null,
                false,
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

    private List<TtsRuleImportItem> CreateImportItems(JsonElement root, IReadOnlyList<HttpTtsRule> existingRules)
    {
        var items = new List<TtsRuleImportItem>();
        var index = 0;

        if (root.ValueKind == JsonValueKind.Object)
        {
            items.Add(CreateImportItem(root, index, existingRules));
            return items;
        }

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

        return items;
    }

    private static bool IsExactDuplicate(IReadOnlyList<HttpTtsRule> existingRules, TtsRuleImportItem item)
    {
        var candidateJson = TtsRuleModelMapper.ExportRuleJson(item.CandidateRule);
        return existingRules.Any(rule =>
            string.Equals(TtsRuleModelMapper.ExportRuleJson(rule), candidateJson, StringComparison.Ordinal));
    }

    private async Task<HttpTtsRule> SaveImportedRuleAsync(
        HttpTtsRule rule,
        IReadOnlyList<HttpTtsRule> existingRules,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow.ToString("O");
        var normalizedRule = EnsureUniqueRuleName(rule, existingRules, null) with
        {
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        var ruleId = await _repository.SaveAsync(normalizedRule, cancellationToken);
        return normalizedRule with { Id = ruleId };
    }

    private static HttpTtsRule EnsureUniqueRuleName(
        HttpTtsRule rule,
        IReadOnlyList<HttpTtsRule> existingRules,
        long? currentRuleId)
    {
        var baseName = string.IsNullOrWhiteSpace(rule.Name) ? "新建规则" : rule.Name.Trim();
        if (!existingRules.Any(existing =>
                existing.Id != currentRuleId &&
                string.Equals(existing.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return rule with { Name = baseName };
        }

        var suffix = 2;
        while (true)
        {
            var candidateName = $"{baseName} ({suffix})";
            if (!existingRules.Any(existing =>
                    existing.Id != currentRuleId &&
                    string.Equals(existing.Name, candidateName, StringComparison.OrdinalIgnoreCase)))
            {
                return rule with { Name = candidateName };
            }

            suffix++;
        }
    }

    private async Task UpdateSelectedRuleAsync(long? ruleId, CancellationToken cancellationToken)
    {
        await _settingsService.UpdateAsync(
            new AppSettingsUpdate
            {
                SelectedTtsRuleId = ruleId,
                ClearSelectedTtsRuleId = ruleId is null
            },
            cancellationToken);
    }

    private async Task ClearSelectedRuleIfNeededAsync(long ruleId, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
        if (settings.SelectedTtsRuleId == ruleId)
        {
            await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    ClearSelectedTtsRuleId = true
                },
                cancellationToken);
        }
    }
}
