namespace NovelSpeaker.Domain.Speech.Providers;

public abstract record SpeechProviderConfiguration(SpeechProviderType Type);

public sealed record HttpSpeechProviderConfiguration(
    string UrlTemplate,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    string? BodyTemplate,
    ProviderRequestRateLimit? RateLimit)
    : SpeechProviderConfiguration(SpeechProviderType.Http);

public sealed record ProviderRequestRateLimit(int MaxRequests, int WindowMilliseconds);
