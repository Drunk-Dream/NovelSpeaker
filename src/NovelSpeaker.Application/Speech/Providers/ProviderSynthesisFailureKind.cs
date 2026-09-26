namespace NovelSpeaker.Application.Speech.Providers;

public enum ProviderSynthesisFailureKind
{
    ProviderUnavailable,
    InvalidRequest,
    Network,
    Timeout,
    InvalidAudio,
    Cancelled,
    Unknown
}
