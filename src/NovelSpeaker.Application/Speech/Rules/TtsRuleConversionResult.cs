using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>
/// Describes the outcome of converting an imported rule into the application's persisted format.
/// </summary>
public sealed record TtsRuleConversionResult(
    HttpTtsRule CandidateRule,
    IReadOnlyList<string> UnsupportedFields,
    IReadOnlyList<string> BlockingIssues)
{
    public bool CanImport => BlockingIssues.Count == 0;

    public TtsRuleCompatibilityStatus CompatibilityStatus => !CanImport
        ? TtsRuleCompatibilityStatus.NeedsManualAdjustment
        : UnsupportedFields.Count == 0
            ? TtsRuleCompatibilityStatus.Compatible
            : TtsRuleCompatibilityStatus.CompatibleWithWarnings;
}
