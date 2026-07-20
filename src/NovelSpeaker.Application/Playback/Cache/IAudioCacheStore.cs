namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Persists and queries the audio cache without exposing storage technology to application use cases.
/// </summary>
public interface IAudioCacheStore
{
    Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
        string bookId,
        CancellationToken cancellationToken);

    Task<AudioCacheStoreCleanupResult> ClearChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken);

    Task<AudioCacheStoreCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken);

    Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken);

    Task RunMaintenanceAsync(CancellationToken cancellationToken);
}
