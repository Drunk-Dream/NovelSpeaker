namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Describes one local audio file resolved through the cache abstraction.
/// </summary>
public sealed record AudioCacheEntry(
    AudioCacheKey Key,
    string FilePath);
