namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Provides cache statistics, cleanup operations, and background maintenance for persisted audio.
/// </summary>
public interface IAudioCacheManagementService
{
    Task<AudioCacheSummary> GetSummaryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedBookSummary>> GetBooksAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedChapterSummary>> GetChaptersAsync(string bookId, CancellationToken cancellationToken);

    Task<AudioCacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken);

    Task<AudioCacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken);

    Task<AudioCacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken);

    Task RunMaintenanceAsync(CancellationToken cancellationToken);
}
