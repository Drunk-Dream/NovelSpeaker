namespace NovelSpeaker.Application.Cache.Audio;

/// <summary>
/// Distinguishes user-blocking audio loads from best-effort prefetch work.
/// </summary>
public enum AudioGenerationPriority
{
    ActiveCache = 0,
    Prefetch = 1,
    Current = 2
}
