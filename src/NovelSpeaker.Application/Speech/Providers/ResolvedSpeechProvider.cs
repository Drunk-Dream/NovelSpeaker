using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

/// <summary>A saved configuration bound to its registered synthesis runtime.</summary>
public sealed record ResolvedSpeechProvider(SpeechProviderInstance Provider, IProviderRuntime Runtime)
{
    public ProviderId ProviderId => Provider.Id;
    public string ProviderName => Provider.Name;
}
