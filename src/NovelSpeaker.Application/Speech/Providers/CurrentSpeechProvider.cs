using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using System.Collections.ObjectModel;

namespace NovelSpeaker.Application.Speech.Providers;

/// <summary>Speech owns the current selection and publishes its semantic changes.</summary>
public sealed class CurrentSpeechProvider : ICurrentSpeechProvider, IDisposable
{
    private readonly IProviderStore _store;
    private readonly IProviderRuntimeResolver _resolver;
    private readonly IAppSettingsService _settings;
    private readonly SpeechProviderWorkspace _workspace;

    public CurrentSpeechProvider(IProviderStore store, IProviderRuntimeResolver resolver,
        IAppSettingsService settings, SpeechProviderWorkspace workspace)
    {
        _store = store;
        _resolver = resolver;
        _settings = settings;
        _workspace = workspace;
        settings.Changed += OnSettingsChanged;
        workspace.Changed += OnProvidersChanged;
    }

    public event EventHandler<SpeechProvidersChangedEventArgs>? Changed;

    public async Task<IReadOnlyList<SpeechProviderInstance>> GetAvailableAsync(CancellationToken cancellationToken) =>
        (await _store.GetAllAsync(cancellationToken).ConfigureAwait(false))
        .Where(provider => _workspace.IsVisible(provider) && ProviderConfigurationValidator.Validate(provider).IsValid)
        .OrderBy(provider => provider.SortOrder).ThenBy(provider => provider.Id.Value).ToArray();

    public async Task<SpeechProviderInstance?> GetConfigurationAsync(ProviderId? providerId, CancellationToken cancellationToken)
    {
        if (providerId is not { } id) return null;
        var provider = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return provider is not null && _workspace.IsVisible(provider) &&
            ProviderConfigurationValidator.Validate(provider).IsValid ? Snapshot(provider) : null;
    }

    public async Task<ResolvedSpeechProvider?> GetSelectedProviderAsync(CancellationToken cancellationToken)
    {
        var resolution = await _resolver.ResolveAsync(_settings.Current.CurrentProviderId, cancellationToken)
            .ConfigureAwait(false);
        return resolution.IsAvailable ? new(Snapshot(resolution.Provider!), resolution.Runtime!) : null;
    }

    public async Task<ResolvedSpeechProvider?> SelectProviderAsync(ProviderId providerId, CancellationToken cancellationToken)
    {
        var resolution = await _resolver.ResolveAsync(providerId, cancellationToken).ConfigureAwait(false);
        if (!resolution.IsAvailable) return null;
        await _settings.UpdateAsync(new AppSettingsUpdate { CurrentProviderId = providerId }, cancellationToken)
            .ConfigureAwait(false);
        return new(Snapshot(resolution.Provider!), resolution.Runtime!);
    }

    internal static SpeechProviderInstance Snapshot(SpeechProviderInstance provider) => provider.Configuration switch
    {
        HttpSpeechProviderConfiguration http => provider with
        {
            Configuration = http with
            {
                Headers = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(http.Headers, StringComparer.OrdinalIgnoreCase))
            }
        },
        _ => provider
    };

    private void OnProvidersChanged(object? sender, SpeechProvidersChangedEventArgs e) => Changed?.Invoke(this, e);
    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs e)
    {
        if (e.Previous.CurrentProviderId != e.Current.CurrentProviderId ||
            !(e.Previous.EnabledExperimentalFeatureIds ?? []).SequenceEqual(e.Current.EnabledExperimentalFeatureIds ?? []))
            Changed?.Invoke(this, new(true));
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _workspace.Changed -= OnProvidersChanged;
    }
}
