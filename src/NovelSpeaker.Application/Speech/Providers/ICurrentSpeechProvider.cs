using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public interface ICurrentSpeechProvider
{
    event EventHandler<SpeechProvidersChangedEventArgs>? Changed;
    Task<IReadOnlyList<SpeechProviderInstance>> GetAvailableAsync(CancellationToken cancellationToken);
    Task<SpeechProviderInstance?> GetConfigurationAsync(ProviderId? providerId, CancellationToken cancellationToken);
    Task<ResolvedSpeechProvider?> GetSelectedProviderAsync(CancellationToken cancellationToken);
    Task<ResolvedSpeechProvider?> SelectProviderAsync(ProviderId providerId, CancellationToken cancellationToken);
}

public sealed record SpeechProvidersChangedEventArgs(bool AffectsSynthesis);
