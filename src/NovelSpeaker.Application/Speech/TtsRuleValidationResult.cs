namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Represents the result of validating a rule editor model before save.
/// </summary>
public sealed record TtsRuleValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    TtsRuleEditorModel NormalizedModel);
