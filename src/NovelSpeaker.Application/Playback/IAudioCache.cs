namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Resolves and stores generated audio by cache key. Full persistence is implemented in a later epic.
/// </summary>
public interface IAudioCache
{
    Task<AudioCacheEntry?> TryGetAsync(AudioCacheKey key, CancellationToken cancellationToken);

    Task<AudioCacheEntry> StoreAsync(AudioCacheKey key, string sourceFilePath, CancellationToken cancellationToken);

    Task InvalidateAsync(AudioCacheKey key, CancellationToken cancellationToken);
}
