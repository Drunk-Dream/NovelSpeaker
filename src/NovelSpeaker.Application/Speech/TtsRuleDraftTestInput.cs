namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Carries the current editor draft and runtime inputs used when testing one rule from the rules page.
/// </summary>
public sealed record TtsRuleDraftTestInput(
    TtsRuleEditorModel Editor,
    string SpeakText,
    int SpeakSpeed);
