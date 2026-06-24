namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Carries the temporary runtime inputs used when testing one rule from the rules page.
/// </summary>
public sealed record TtsRuleTestInput(
    long RuleId,
    string SpeakText,
    int SpeakSpeed,
    IReadOnlyDictionary<string, string> LoginInfo);
