namespace NovelSpeaker.Application.Speech.Rules;

internal sealed class TtsRuleQueries : ITtsRuleQueries
{
    public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<TtsRuleSummary>>([]);
    }

    public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
