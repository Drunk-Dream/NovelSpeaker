using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Rules;

internal sealed class TtsRuleImportUseCase(
    ITtsRuleRepository repository,
    ITtsRuleSourceAdapter sourceAdapter,
    IAppSettingsService settingsService,
    ITtsRuleSelectionUseCase selection,
    TimeProvider timeProvider) : ITtsRuleImportUseCase
{
    public async Task<TtsRuleImportPreview> CreateImportPreviewAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = sourceAdapter.Read(jsonText);
        if (source.ErrorMessage is not null)
        {
            return new TtsRuleImportPreview(sourceDescription, [], source.ErrorMessage);
        }

        var existing = await repository.GetAllAsync(cancellationToken);
        return new TtsRuleImportPreview(sourceDescription, source.Items.Select(item => CreateItem(item, existing)).ToArray(), null);
    }

    public async Task<TtsRuleImportResult> ImportJsonTextAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken) =>
        await ImportAsync(await CreateImportPreviewAsync(jsonText, sourceDescription, cancellationToken), cancellationToken);

    public async Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken)
    {
        if (preview.ErrorMessage is not null)
        {
            return new TtsRuleImportResult(0, 0, preview.Items.Count) { FailedCount = preview.Items.Count };
        }

        var existing = (await repository.GetAllAsync(cancellationToken)).ToList();
        var imported = 0;
        var skipped = 0;
        var failed = 0;
        long? first = null;
        long? firstEnabled = null;
        foreach (var item in preview.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDuplicate(existing, item.CandidateRule))
            {
                skipped++;
                continue;
            }

            if (!item.CanImport)
            {
                failed++;
                continue;
            }

            var now = timeProvider.GetUtcNow();
            var rule = TtsRuleModelMapper.EnsureUniqueName(item.CandidateRule, existing, null) with { CreatedAt = now, UpdatedAt = now };
            var id = await repository.SaveAsync(rule, cancellationToken);
            rule = rule with { Id = id };
            existing.Add(rule);
            first ??= id;
            if (rule.IsEnabled)
            {
                firstEnabled ??= id;
            }

            imported++;
        }

        if (firstEnabled is not null && settingsService.Current.SelectedTtsRuleId is null)
        {
            await selection.SelectRuleAsync(firstEnabled.Value, cancellationToken);
        }

        return new TtsRuleImportResult(imported, skipped, preview.Items.Count) { FailedCount = failed, FirstImportedRuleId = first };
    }

    private TtsRuleImportItem CreateItem(TtsRuleSourceItem source, IReadOnlyList<HttpTtsRule> existing)
    {
        if (source.Conversion is null)
        {
            var now = timeProvider.GetUtcNow();
            return new TtsRuleImportItem(source.Index, $"无效规则 #{source.Index + 1}", string.Empty,
                TtsRuleCompatibilityStatus.NeedsManualAdjustment, [], false, false, false, source.ErrorMessage!,
                new HttpTtsRule(0, string.Empty, string.Empty, null, null, new Dictionary<string, string>(), null, null, false, null, false, null, now, now));
        }
        var conversion = source.Conversion;
        var duplicate = IsDuplicate(existing, conversion.CandidateRule);
        var nameConflict = !string.IsNullOrWhiteSpace(conversion.CandidateRule.Name) &&
            existing.Any(rule => string.Equals(rule.Name, conversion.CandidateRule.Name, StringComparison.OrdinalIgnoreCase)) && !duplicate;
        var message = duplicate ? "与现有规则完全相同，将跳过导入。" : nameConflict ? "名称与现有规则重复，但内容不同，将作为新规则导入。" :
            conversion.BlockingIssues.Count > 0 ? string.Join(" ", conversion.BlockingIssues) :
            conversion.CompatibilityStatus == TtsRuleCompatibilityStatus.Compatible ? "可直接导入。" :
            $"可导入，但包含未支持字段：{string.Join("、", conversion.UnsupportedFields)}。";
        var candidate = conversion.CandidateRule;
        return new TtsRuleImportItem(source.Index, string.IsNullOrWhiteSpace(candidate.Name) ? $"未命名规则 #{source.Index + 1}" : candidate.Name,
            candidate.Url, conversion.CompatibilityStatus, conversion.UnsupportedFields, conversion.CanImport && !duplicate,
            duplicate, nameConflict, message, candidate);
    }

    private static bool IsDuplicate(IEnumerable<HttpTtsRule> existing, HttpTtsRule candidate)
    {
        var json = TtsRuleJsonSerializer.Serialize(candidate);
        return existing.Any(rule => string.Equals(TtsRuleJsonSerializer.Serialize(rule), json, StringComparison.Ordinal));
    }
}
