namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Storage-facing outcome of deleting persisted audio cache entries.
/// </summary>
public sealed record AudioCacheStoreCleanupResult(
    long DeletedBytes,
    int DeletedEntryCount,
    int ProtectedEntryCount,
    int FailedEntryCount);
