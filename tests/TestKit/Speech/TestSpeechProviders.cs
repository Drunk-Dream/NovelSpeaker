using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Application.Speech.Providers;

namespace NovelSpeaker.TestKit.Speech;

internal static class TestSpeechProviders
{
    public static SpeechProviderInstance Create(long id, string name, string url) =>
        Item(id, name) with { Configuration = new HttpSpeechProviderConfiguration(url, "GET", new Dictionary<string, string>(), null, null) };

    public static ProviderId Id(long value) => new(new Guid((int)value, 0, 0, new byte[8]));
    public static ResolvedSpeechProvider Resolve(SpeechProviderInstance provider) => new(provider, new Runtime());
    public static SpeechProviderInstance Item(long id, string name, bool configured = true) => new(Id(id), name, (int)id,
        new HttpSpeechProviderConfiguration(configured ? $"https://cache-key.invalid/{id}" : "", "GET", new Dictionary<string, string>(), null, null),
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private sealed class Runtime : IProviderRuntime
    {
        public SpeechProviderType Type => SpeechProviderType.Http;
        public Task<ProviderSynthesisResult> SynthesizeAsync(SpeechProviderInstance provider, ProviderSynthesisRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

internal class TestCurrentSpeechProvider : ICurrentSpeechProvider
{
    public virtual event EventHandler<SpeechProvidersChangedEventArgs>? Changed { add { } remove { } }
    public virtual Task<IReadOnlyList<SpeechProviderInstance>> GetAvailableAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SpeechProviderInstance>>([]);
    public virtual async Task<SpeechProviderInstance?> GetConfigurationAsync(ProviderId? providerId, CancellationToken cancellationToken) => (await GetSelectedProviderAsync(cancellationToken))?.Provider;
    public virtual Task<ResolvedSpeechProvider?> GetSelectedProviderAsync(CancellationToken cancellationToken) => Task.FromResult<ResolvedSpeechProvider?>(null);
    public virtual Task<ResolvedSpeechProvider?> SelectProviderAsync(ProviderId providerId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
