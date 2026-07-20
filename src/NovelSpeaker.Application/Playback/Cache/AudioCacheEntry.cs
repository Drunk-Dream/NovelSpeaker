namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Describes one local audio file resolved through the cache abstraction.
/// </summary>
public sealed record AudioCacheEntry(
    AudioCacheKey Key,
    string FilePath);
