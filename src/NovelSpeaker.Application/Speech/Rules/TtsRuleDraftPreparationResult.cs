using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>Returns a validated, normalized draft rule for transient execution without persisting it.</summary>
public sealed record TtsRuleDraftPreparationResult(
    TtsRuleValidationResult Validation,
    HttpTtsRule? CandidateRule)
{
    public bool IsValid => Validation.IsValid && CandidateRule is not null;
}
