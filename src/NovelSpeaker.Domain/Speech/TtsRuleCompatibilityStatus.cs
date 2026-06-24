namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Describes how well an imported TTS rule fits the current MVP compatibility envelope.
/// </summary>
public enum TtsRuleCompatibilityStatus
{
    Compatible = 0,
    CompatibleWithWarnings = 1,
    NeedsManualAdjustment = 2
}
