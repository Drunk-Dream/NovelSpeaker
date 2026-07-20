namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Represents cleanup outcome projected for cache UI feedback.
/// </summary>
public sealed record CacheCleanupResult(
    long DeletedBytes,
    int DeletedEntryCount,
    int ProtectedEntryCount,
    int FailedEntryCount)
{
    public bool HasWarnings => ProtectedEntryCount > 0 || FailedEntryCount > 0;
}
