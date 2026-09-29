namespace NovelSpeaker.Domain.Speech.Providers;

public abstract record SpeechProviderConfiguration(SpeechProviderType Type);

public sealed record HttpSpeechProviderConfiguration(
    string UrlTemplate,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    string? BodyTemplate,
    ProviderRequestRateLimit? RateLimit)
    : SpeechProviderConfiguration(SpeechProviderType.Http);

public sealed record EdgeVoice(string VoiceId, string FriendlyName, string Locale, string Gender);

public sealed record EdgeSpeechProviderConfiguration(EdgeVoice? Voice)
    : SpeechProviderConfiguration(SpeechProviderType.MicrosoftEdge);

public sealed record ProviderRequestRateLimit(int MaxRequests, int WindowMilliseconds);
