namespace NovelSpeaker.Domain.Speech.Providers;

public sealed record SpeechProviderInstance(
    ProviderId Id,
    string Name,
    int SortOrder,
    SpeechProviderConfiguration Configuration,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public SpeechProviderType Type => Configuration.Type;
}
