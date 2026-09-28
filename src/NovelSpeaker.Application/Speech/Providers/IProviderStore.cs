using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public interface IProviderStore
{
    Task<IReadOnlyList<SpeechProviderInstance>> GetAllAsync(CancellationToken cancellationToken);

    Task<SpeechProviderInstance?> GetByIdAsync(ProviderId providerId, CancellationToken cancellationToken);

    Task SaveAsync(SpeechProviderInstance provider, CancellationToken cancellationToken);

    Task InsertAfterAsync(SpeechProviderInstance provider, ProviderId precedingId, CancellationToken cancellationToken);

    Task UpdateSortOrderAsync(IReadOnlyList<ProviderId> orderedIds, CancellationToken cancellationToken);

    Task DeleteAsync(ProviderId providerId, CancellationToken cancellationToken);
}
