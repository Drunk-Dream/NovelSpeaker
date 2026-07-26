namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Identifies the caller class waiting for a shared per-rule TTS admission slot.
/// </summary>
public enum TtsAdmissionPriority
{
    ActiveCache = 0,
    Prefetch = 1,
    CurrentPlayback = 2
}
