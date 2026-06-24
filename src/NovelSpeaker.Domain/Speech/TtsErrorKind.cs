namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Classifies user-visible HTTP TTS failures.
/// </summary>
public enum TtsErrorKind
{
    Network = 0,
    Timeout = 1,
    Unauthorized = 2,
    RateLimited = 3,
    ServerError = 4,
    InvalidRule = 5,
    ScriptError = 6,
    InvalidResponse = 7,
    AudioDecode = 8,
    Cancelled = 9,
    Unknown = 10
}
