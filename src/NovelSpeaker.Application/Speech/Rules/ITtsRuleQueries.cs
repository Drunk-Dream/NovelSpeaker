namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>Provides read-only projections and canonical export for persisted TTS rules.</summary>
public interface ITtsRuleQueries
{
    Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken);

    Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken);
}
