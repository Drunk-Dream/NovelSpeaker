namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Tracks cache files that must be excluded from concurrent cleanup operations.
/// </summary>
public interface IAudioCacheProtectionRegistry
{
    IDisposable Protect(string filePath);

    bool IsProtected(string filePath);
}
