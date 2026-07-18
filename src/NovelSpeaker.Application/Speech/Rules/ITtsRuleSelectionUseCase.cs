namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>Provides the single write boundary for current-rule selection and protected mutations.</summary>
public interface ITtsRuleSelectionUseCase
{
    Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken);

    Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(long ruleId, TtsRuleMutationAction action, CancellationToken cancellationToken);

    Task<TtsRuleMutationResult> ApplyRuleMutationAsync(TtsRuleMutationDecision decision, CancellationToken cancellationToken);
}
