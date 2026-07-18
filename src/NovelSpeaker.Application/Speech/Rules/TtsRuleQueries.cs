using NovelSpeaker.Application.Settings;

namespace NovelSpeaker.Application.Speech.Rules;

internal sealed class TtsRuleQueries(ITtsRuleRepository repository, IAppSettingsService settingsService) : ITtsRuleQueries
{
    public async Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await repository.GetAllAsync(cancellationToken);
        return rules.Select(rule => TtsRuleModelMapper.ToSummary(rule, settingsService.Current.SelectedTtsRuleId)).ToArray();
    }

    public async Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(ruleId, cancellationToken);
        return rule is null ? null : TtsRuleJsonSerializer.Serialize(rule);
    }
}
