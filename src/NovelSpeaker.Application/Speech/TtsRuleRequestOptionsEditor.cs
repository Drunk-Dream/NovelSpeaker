namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Represents the editable requestOptions projection of a TTS rule.
/// </summary>
public sealed record TtsRuleRequestOptionsEditor(
    string? Method,
    string? Body);
