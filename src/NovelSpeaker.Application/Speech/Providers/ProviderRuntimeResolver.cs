using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Application.Settings;

namespace NovelSpeaker.Application.Speech.Providers;

public sealed class ProviderRuntimeResolver : IProviderRuntimeResolver
{
    private readonly IProviderStore _providers;
    private readonly IProviderRuntime? _httpRuntime;
    private readonly IProviderRuntime? _edgeRuntime;
    private readonly ExperimentalFeaturesService? _features;

    public ProviderRuntimeResolver(IProviderStore providers, IProviderRuntime? httpRuntime = null,
        IProviderRuntime? edgeRuntime = null, ExperimentalFeaturesService? features = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _httpRuntime = httpRuntime;
        _edgeRuntime = edgeRuntime;
        _features = features;
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

        if (provider.Type == SpeechProviderType.MicrosoftEdge &&
            _features?.IsEnabled(ExperimentalFeaturesService.MicrosoftEdgeTts) != true)
            return Unavailable(ProviderRuntimeUnavailableReason.ProviderHidden, provider);

        var runtime = provider.Type switch
        {
            SpeechProviderType.Http => _httpRuntime,
            SpeechProviderType.MicrosoftEdge => _edgeRuntime,
            _ => null
        };
        if (runtime is null)
        {
            return Unavailable(ProviderRuntimeUnavailableReason.RuntimeUnavailable, provider);
        }

        if (runtime.Type != provider.Type)
        {
            return Unavailable(ProviderRuntimeUnavailableReason.UnsupportedProviderType, provider);
        }

        return new ProviderRuntimeResolution(provider, runtime, null);
    }

    private static ProviderRuntimeResolution Unavailable(
        ProviderRuntimeUnavailableReason reason,
        SpeechProviderInstance? provider = null) =>
        new(provider, null, reason);
}
