using NovelSpeaker.Application.Settings;

namespace NovelSpeaker.Application.Speech.Rules;

internal sealed class TtsRuleSelectionUseCase(
    ITtsRuleRepository repository,
    IAppSettingsService settingsService,
    ITtsRuleQueries queries,
    TimeProvider timeProvider) : ITtsRuleSelectionUseCase
{
    public async Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken)
    {
        if (ruleId is null)
        {
            await UpdateSelectedRuleAsync(null, cancellationToken);
            return;
        }

        var rule = await repository.GetByIdAsync(ruleId.Value, cancellationToken);
        if (rule is null || !rule.IsEnabled)
        {
            throw new InvalidOperationException("只能将存在且已启用的规则设为当前规则。");
        }

        var utcNow = timeProvider.GetUtcNow();
        await repository.SaveAsync(rule with { LastUsedAt = utcNow, UpdatedAt = utcNow }, cancellationToken);
        await UpdateSelectedRuleAsync(rule.Id, cancellationToken);
    }

    public async Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(long ruleId, TtsRuleMutationAction action, CancellationToken cancellationToken)
    {
        var isCurrentRule = settingsService.Current.SelectedTtsRuleId == ruleId;
        var rules = await queries.GetRulesAsync(cancellationToken);
        return new TtsRuleProtectionInfo(ruleId, action, isCurrentRule, !isCurrentRule, isCurrentRule,
            rules.Where(rule => rule.Id != ruleId && rule.IsEnabled).ToArray());
    }

    public async Task<TtsRuleMutationResult> ApplyRuleMutationAsync(TtsRuleMutationDecision decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var protection = await GetRuleProtectionAsync(decision.RuleId, decision.Action, cancellationToken);
        if (protection.IsCurrentRule && !protection.CanApplyDirectly)
        {
            if (decision.ReplacementRuleId is not null)
            {
                var replacement = await repository.GetByIdAsync(decision.ReplacementRuleId.Value, cancellationToken);
                if (replacement is null || !replacement.IsEnabled || replacement.Id == decision.RuleId)
                {
                    throw new InvalidOperationException("必须选择另一条已启用规则作为替代规则。");
                }
            }
            else if (!decision.ClearSelectedRule || !protection.CanClearSelectedRule)
            {
                throw new InvalidOperationException("当前规则需要明确清空当前规则后才能继续。");
            }
        }

        if (decision.Action == TtsRuleMutationAction.Disable)
        {
            var rule = await repository.GetByIdAsync(decision.RuleId, cancellationToken)
                ?? throw new InvalidOperationException("未找到要禁用的规则。");
            await repository.SaveAsync(rule with { IsEnabled = false, UpdatedAt = timeProvider.GetUtcNow() }, cancellationToken);
        }
        else if (decision.Action == TtsRuleMutationAction.Delete)
        {
            await repository.DeleteAsync(decision.RuleId, cancellationToken);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision.Action, null);
        }

        if (decision.ReplacementRuleId is not null)
        {
            await SelectRuleAsync(decision.ReplacementRuleId, cancellationToken);
            return new TtsRuleMutationResult(decision.RuleId, decision.Action, decision.ReplacementRuleId, false);
        }

        if (protection.IsCurrentRule)
        {
            await UpdateSelectedRuleAsync(null, cancellationToken);
        }

        return new TtsRuleMutationResult(decision.RuleId, decision.Action, settingsService.Current.SelectedTtsRuleId,
            protection.IsCurrentRule && settingsService.Current.SelectedTtsRuleId is null);
    }

    private Task UpdateSelectedRuleAsync(long? ruleId, CancellationToken cancellationToken) =>
        settingsService.UpdateAsync(new AppSettingsUpdate
        {
            SelectedTtsRuleId = ruleId,
            ClearSelectedTtsRuleId = ruleId is null
        }, cancellationToken);
}
