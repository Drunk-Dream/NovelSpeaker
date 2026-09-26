namespace NovelSpeaker.Application.Speech.Rules;

internal sealed class TtsRuleSelectionUseCase : ITtsRuleSelectionUseCase
{
    private const string UnavailableMessage =
        "Legacy TTS rule selection is unavailable while speech providers are being migrated.";

    public Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException(UnavailableMessage));

    public Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(long ruleId, TtsRuleMutationAction action, CancellationToken cancellationToken) =>
        throw new NotSupportedException(UnavailableMessage);

    public Task<TtsRuleMutationResult> ApplyRuleMutationAsync(TtsRuleMutationDecision decision, CancellationToken cancellationToken) =>
        Task.FromException<TtsRuleMutationResult>(new NotSupportedException(UnavailableMessage));
}
