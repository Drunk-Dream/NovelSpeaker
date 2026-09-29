namespace NovelSpeaker.Application.Speech.Providers;

public enum ProviderSynthesisFailureKind
{
    ProviderUnavailable,
    InvalidRequest,
    Network,
    Timeout,
    RateLimited,
    EmptyAudio,
    InvalidAudio,
    Cancelled,
    Unknown
}
