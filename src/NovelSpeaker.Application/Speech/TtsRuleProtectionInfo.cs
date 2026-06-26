using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Describes whether a selected-rule mutation can proceed directly or needs a replacement choice.
/// </summary>
public sealed record TtsRuleProtectionInfo(
    long RuleId,
    TtsRuleMutationAction Action,
    bool IsCurrentRule,
    bool CanApplyDirectly,
    bool CanClearSelectedRule,
    IReadOnlyList<TtsRuleSummary> ReplacementCandidates);
