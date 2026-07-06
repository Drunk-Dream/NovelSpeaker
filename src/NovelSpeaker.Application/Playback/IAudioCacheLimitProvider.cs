namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Resolves the currently effective persisted audio cache limit.
/// </summary>
public interface IAudioCacheLimitProvider
{
    long GetCurrentLimitBytes();
}
