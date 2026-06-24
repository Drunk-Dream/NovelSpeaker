using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Placeholder cache implementation used until the persistent cache epic is implemented.
/// </summary>
public sealed class NoOpAudioCache : IAudioCache
{
    public Task<AudioCacheEntry?> TryGetAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<AudioCacheEntry?>(null);
    }

    public Task<AudioCacheEntry> StoreAsync(AudioCacheKey key, string sourceFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AudioCacheEntry(key, sourceFilePath));
    }

    public Task InvalidateAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
