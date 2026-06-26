namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Describes how a protected rule mutation should be applied.
/// </summary>
public sealed record TtsRuleMutationDecision(
    long RuleId,
    TtsRuleMutationAction Action,
    long? ReplacementRuleId,
    bool ClearSelectedRule);
