namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Describes one validated local audio file returned from an HTTP TTS backend.
/// </summary>
public sealed record TtsAudioResponse(
    string FilePath,
    int StatusCode,
    string? ResponseContentType,
    string? DetectedAudioFormat);
