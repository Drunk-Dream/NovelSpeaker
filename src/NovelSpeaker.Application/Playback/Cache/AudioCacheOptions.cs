namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Provides process-wide cache limits for persisted playback audio.
/// </summary>
public sealed record AudioCacheOptions(long MaxCacheSizeBytes)
{
    public const long DefaultMaxCacheSizeBytes = 2L * 1024 * 1024 * 1024;

    public static AudioCacheOptions Default { get; } = new(DefaultMaxCacheSizeBytes);

    public AudioCacheOptions Normalize()
    {
        return this with
        {
            MaxCacheSizeBytes = MaxCacheSizeBytes > 0 ? MaxCacheSizeBytes : DefaultMaxCacheSizeBytes
        };
    }
}
