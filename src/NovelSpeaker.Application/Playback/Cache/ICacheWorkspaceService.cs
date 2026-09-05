namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Provides UI-facing cache overview, cache listings, completeness estimates, and cleanup actions.
/// </summary>
public interface ICacheWorkspaceService
{
    event EventHandler<CacheChangedEventArgs>? Changed;

    Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken);

    Task<CachedBookCacheItem?> GetCachedBookAsync(
        string bookId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(string bookId, CancellationToken cancellationToken);

    Task<CachedChapterCacheItem?> GetCachedChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken);

    async Task<IReadOnlyList<CachedChapterCatalogEntry>> GetCachedChapterCatalogAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        var chapters = await GetCachedChaptersAsync(bookId, cancellationToken).ConfigureAwait(false);
        return chapters
            .Select(static chapter => new CachedChapterCatalogEntry(
                chapter.BookId,
                chapter.ChapterIndex,
                chapter.Title))
            .ToArray();
    }

    async Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChapterDecorationsAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chapterIndices);
        var requested = chapterIndices.ToHashSet();
        if (requested.Count == 0)
        {
            return [];
        }

        var chapters = new List<CachedChapterCacheItem>(requested.Count);
        foreach (var chapterIndex in requested.Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await GetCachedChapterAsync(bookId, chapterIndex, cancellationToken).ConfigureAwait(false) is { } chapter)
            {
                chapters.Add(chapter);
            }
        }

        return chapters;
    }

    Task<IReadOnlyList<ChapterCacheStatus>> GetChapterCacheStatusesAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken);

    Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken);

    Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken);

    Task<CacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken);

    Task<CacheCleanupResult> ClearChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken);

    Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken);
}
