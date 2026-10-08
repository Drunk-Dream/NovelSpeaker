namespace NovelSpeaker.Application.Cache;

/// <summary>Cache-owned, current-configuration views. Queries never await plan repair.</summary>
public interface ICacheReadModel
{
    long Revision { get; }

    event EventHandler<CacheReadModelChange>? Changed;

    Task<CacheReadResult<CacheOverviewModel>> GetOverviewAsync(CancellationToken cancellationToken);

    Task<CacheReadResult<IReadOnlyList<CachedBookSummary>>> GetBooksAsync(
        CancellationToken cancellationToken);

    Task<CacheReadResult<IReadOnlyList<CachedBookSummary>>> GetBooksAsync(
        IReadOnlyCollection<string> bookIds, CancellationToken cancellationToken);

    Task<CacheReadResult<CachedBookCatalog>> GetBookAsync(
        string bookId, CancellationToken cancellationToken);

    Task<CacheReadResult<IReadOnlyList<CacheChapterView>>> GetChaptersAsync(
        string bookId, IReadOnlyCollection<int> chapterIndices, CancellationToken cancellationToken);
}
