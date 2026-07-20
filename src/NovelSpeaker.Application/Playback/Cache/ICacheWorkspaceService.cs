namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Provides UI-facing cache overview, cache listings, completeness estimates, and cleanup actions.
/// </summary>
public interface ICacheWorkspaceService
{
    Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(string bookId, CancellationToken cancellationToken);

    Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken);

    Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken);

    Task<CacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken);

    Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken);
}
