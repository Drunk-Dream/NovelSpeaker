using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

/// <summary>Process-only catalog cache. Failed refreshes never replace an existing snapshot.</summary>
public sealed class EdgeVoiceCatalog(IEdgeSpeechTransport transport, TimeProvider timeProvider)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<EdgeVoice> _voices = [];
    private DateTimeOffset? _loadedAt;
    public IReadOnlyList<EdgeVoice> Current => Volatile.Read(ref _voices);

    public async Task<IReadOnlyList<EdgeVoice>> GetAsync(bool refresh, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh && _loadedAt is { } loaded && timeProvider.GetUtcNow() - loaded < TimeSpan.FromHours(1))
                return Current;
            var voices = await transport.GetVoicesAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var ordered = await Task.Run(() => Array.AsReadOnly(voices
                .OrderBy(voice => voice.Locale, StringComparer.Ordinal)
                .ThenBy(voice => voice.FriendlyName, StringComparer.Ordinal)
                .ThenBy(voice => voice.VoiceId, StringComparer.Ordinal).ToArray()), cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _voices, ordered);
            _loadedAt = timeProvider.GetUtcNow();
            return ordered;
        }
        finally { _gate.Release(); }
    }
}
