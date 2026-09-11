using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Composes physical cache facts with detached book metadata without calculating Coverage.
/// </summary>
public sealed class CacheCatalog : ICacheCatalog
{
    private readonly IAudioCacheStore _cacheStore;
    private readonly IBookPlaybackMetadataQuery _bookMetadataQuery;
    private readonly IBookLibraryQuery? _bookLibraryQuery;

    public CacheCatalog(
        IAudioCacheStore cacheStore,
        IBookPlaybackMetadataQuery bookMetadataQuery,
        IBookLibraryQuery? bookLibraryQuery = null)
    {
        _cacheStore = cacheStore;
        _bookMetadataQuery = bookMetadataQuery;
        _bookLibraryQuery = bookLibraryQuery;
    }

    public async Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var summary = await _cacheStore.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
        return new CacheOverviewModel(
            summary.TotalSizeBytes,
            summary.EntryCount,
            summary.LimitBytes,
            summary.IsOverLimit);
    }

    public async Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(
        CancellationToken cancellationToken)
    {
        var summaries = await _cacheStore.GetBooksAsync(cancellationToken).ConfigureAwait(false);
        return await ComposeBookSummariesAsync(summaries, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(
        IReadOnlyCollection<string> bookIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookIds);
        var summaries = await _cacheStore
            .GetBooksAsync(bookIds, cancellationToken)
            .ConfigureAwait(false);
        return await ComposeBookSummariesAsync(summaries, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CachedBookSummary>> ComposeBookSummariesAsync(
        IReadOnlyList<CachedBookStoreSummary> summaries,
        CancellationToken cancellationToken)
    {
        if (summaries.Count == 0)
        {
            return [];
        }

        var metadataById = _bookLibraryQuery is null
            ? null
            : (await _bookLibraryQuery.GetBooksAsync(
                    summaries.Select(static summary => summary.BookId).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false))
                .ToDictionary(book => book.Id, StringComparer.Ordinal);
        var books = new List<CachedBookSummary>(summaries.Count);
        foreach (var summary in summaries)
        {
            var metadata = metadataById?.GetValueOrDefault(summary.BookId);
            if (metadata is null)
            {
                metadata = await _bookMetadataQuery
                    .GetBookAsync(summary.BookId, cancellationToken)
                    .ConfigureAwait(false)
                    is { } playbackMetadata
                    ? new BookSummary(
                        playbackMetadata.BookId,
                        playbackMetadata.Title,
                        playbackMetadata.Author,
                        "未开始",
                        DateTimeOffset.MinValue)
                    : null;
            }

            books.Add(new CachedBookSummary(
                summary.BookId,
                metadata?.Title ?? summary.BookId,
                metadata?.Author,
                summary.ChapterCount,
                summary.EntryCount,
                summary.TotalSizeBytes));
        }

        return Array.AsReadOnly(books.ToArray());
    }

    public async Task<CachedBookSummary?> GetCachedBookAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var summary = await _cacheStore
            .GetBookAsync(bookId, cancellationToken)
            .ConfigureAwait(false);
        if (summary is null)
        {
            return null;
        }

        var metadata = await _bookMetadataQuery
            .GetBookAsync(summary.BookId, cancellationToken)
            .ConfigureAwait(false);
        return new CachedBookSummary(
            summary.BookId,
            metadata?.Title ?? summary.BookId,
            metadata?.Author,
            summary.ChapterCount,
            summary.EntryCount,
            summary.TotalSizeBytes);
    }

    public async Task<IReadOnlyList<CachedChapterCatalogEntry>> GetCachedChapterCatalogAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var summaries = await _cacheStore
            .GetChaptersAsync(bookId, cancellationToken)
            .ConfigureAwait(false);
        if (summaries.Count == 0)
        {
            return [];
        }

        var metadata = await _bookMetadataQuery
            .GetChaptersAsync(
                bookId,
                summaries.Select(static summary => summary.ChapterIndex).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        var titlesByIndex = metadata.ToDictionary(
            static chapter => chapter.ChapterIndex,
            static chapter => chapter.Title);

        return Array.AsReadOnly(summaries
            .OrderBy(static summary => summary.ChapterIndex)
            .Select(summary => new CachedChapterCatalogEntry(
                summary.BookId,
                summary.ChapterIndex,
                titlesByIndex.GetValueOrDefault(
                    summary.ChapterIndex,
                    $"第 {summary.ChapterIndex + 1} 章")))
            .ToArray());
    }

    public async Task<IReadOnlyList<CachedChapterSummary>> GetCachedChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);

        var requested = chapterIndices
            .Distinct()
            .Order()
            .ToArray();
        if (requested.Length == 0)
        {
            return [];
        }

        if (requested[0] < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chapterIndices));
        }

        var summaries = await _cacheStore
            .GetChaptersAsync(bookId, requested, cancellationToken)
            .ConfigureAwait(false);
        if (summaries.Count == 0)
        {
            return [];
        }

        var metadata = await _bookMetadataQuery
            .GetChaptersAsync(
                bookId,
                summaries.Select(static summary => summary.ChapterIndex).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        var titlesByIndex = metadata.ToDictionary(
            static chapter => chapter.ChapterIndex,
            static chapter => chapter.Title);

        return Array.AsReadOnly(summaries
            .OrderBy(static summary => summary.ChapterIndex)
            .Select(summary => new CachedChapterSummary(
                summary.BookId,
                summary.ChapterIndex,
                titlesByIndex.GetValueOrDefault(
                    summary.ChapterIndex,
                    $"第 {summary.ChapterIndex + 1} 章"),
                summary.DistinctSegmentCount,
                summary.EntryCount,
                summary.TotalSizeBytes))
            .ToArray());
    }

    public async Task<CachedChapterSummary?> GetCachedChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        var chapters = await GetCachedChaptersAsync(
                bookId,
                [chapterIndex],
                cancellationToken)
            .ConfigureAwait(false);
        return chapters.FirstOrDefault();
    }
}
