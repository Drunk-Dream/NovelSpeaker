namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Represents one editable key-value entry in a rule editor.
/// </summary>
public sealed record TtsRuleEditorKeyValue(
    string Key,
    string Value);
