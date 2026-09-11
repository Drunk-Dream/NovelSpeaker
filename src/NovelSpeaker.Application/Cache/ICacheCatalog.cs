namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Provides immutable physical cache read models for application scenarios.
/// </summary>
public interface ICacheCatalog
{
    Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(
        IReadOnlyCollection<string> bookIds,
        CancellationToken cancellationToken);

    Task<CachedBookSummary?> GetCachedBookAsync(
        string bookId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedChapterCatalogEntry>> GetCachedChapterCatalogAsync(
        string bookId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CachedChapterSummary>> GetCachedChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken);

    Task<CachedChapterSummary?> GetCachedChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken);
}
