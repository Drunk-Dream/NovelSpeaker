namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Summarizes one cache cleanup operation.
/// </summary>
public sealed record AudioCacheCleanupResult(
    long DeletedBytes,
    int DeletedEntryCount,
    int ProtectedEntryCount,
    int FailedEntryCount);
