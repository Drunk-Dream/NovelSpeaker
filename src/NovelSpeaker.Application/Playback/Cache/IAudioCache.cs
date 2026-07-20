namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Resolves and stores generated audio by cache key.
/// </summary>
public interface IAudioCache
{
    Task<AudioCacheEntry?> TryGetAsync(AudioCacheKey key, CancellationToken cancellationToken);

    Task<AudioCacheEntry> StoreAsync(AudioCacheWriteRequest request, CancellationToken cancellationToken);

    Task InvalidateAsync(AudioCacheKey key, CancellationToken cancellationToken);
}
