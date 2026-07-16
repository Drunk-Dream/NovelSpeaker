using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Compilation;

/// <summary>
/// Provides the read-only values exposed to converted rule templates.
/// </summary>
public sealed record TtsRuleContext(
    string SpeakText,
    int SpeakSpeed,
    HttpTtsRule Source);
