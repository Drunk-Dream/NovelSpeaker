using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public sealed class ProviderRuntimeResolver : IProviderRuntimeResolver
{
    private readonly IProviderStore _providers;
    private readonly IProviderRuntime _httpRuntime;

    public ProviderRuntimeResolver(IProviderStore providers, IProviderRuntime httpRuntime)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _httpRuntime = httpRuntime ?? throw new ArgumentNullException(nameof(httpRuntime));
    }

    public async Task<ProviderRuntimeResolution> ResolveAsync(
        ProviderId? providerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (providerId is null)
        {
            return Unavailable(ProviderRuntimeUnavailableReason.NoCurrentProvider);
        }

        var provider = await _providers.GetByIdAsync(providerId.Value, cancellationToken).ConfigureAwait(false);
        if (provider is null)
        {
            return Unavailable(ProviderRuntimeUnavailableReason.ProviderNotFound);
        }

        if (!ProviderConfigurationValidator.Validate(provider).IsValid)
        {
            return Unavailable(ProviderRuntimeUnavailableReason.ProviderNotConfigured, provider);
        }

        if (provider.Type != SpeechProviderType.Http || _httpRuntime.Type != provider.Type)
        {
            return Unavailable(ProviderRuntimeUnavailableReason.UnsupportedProviderType, provider);
        }

        return new ProviderRuntimeResolution(provider, _httpRuntime, null);
    }

    private static ProviderRuntimeResolution Unavailable(
        ProviderRuntimeUnavailableReason reason,
        SpeechProviderInstance? provider = null) =>
        new(provider, null, reason);
}
