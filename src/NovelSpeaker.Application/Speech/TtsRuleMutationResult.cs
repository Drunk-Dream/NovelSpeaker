namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Represents the outcome of a protected rule mutation.
/// </summary>
public sealed record TtsRuleMutationResult(
    long RuleId,
    TtsRuleMutationAction Action,
    long? SelectedRuleId,
    bool ClearedSelectedRule);
