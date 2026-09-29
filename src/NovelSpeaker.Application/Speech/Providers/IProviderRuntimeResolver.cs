using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public interface IProviderRuntimeResolver
{
    Task<ProviderRuntimeResolution> ResolveAsync(
        ProviderId? providerId,
        CancellationToken cancellationToken);
}
