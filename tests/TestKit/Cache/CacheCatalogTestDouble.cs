using NovelSpeaker.Application.Cache;

namespace NovelSpeaker.TestKit.Cache;

internal sealed class CacheCatalogTestDouble : ICacheCatalog
{
    private readonly Func<CancellationToken, Task<CacheOverviewModel>>? _overviewFactory;

    public CacheCatalogTestDouble(Func<CancellationToken, Task<CacheOverviewModel>>? overviewFactory = null)
    {
        _overviewFactory = overviewFactory;
    }

    public CacheOverviewModel Overview { get; set; } = new(0, 0, 0, false);

    public IReadOnlyList<CachedBookSummary> Books { get; set; } = [];

    public Dictionary<string, IReadOnlyList<CachedChapterSummary>> ChaptersByBook { get; } =
        new(StringComparer.Ordinal);

    public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) =>
        _overviewFactory is null
            ? Task.FromResult(Overview)
            : _overviewFactory(cancellationToken);

    public Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Books);

    public Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(
        IReadOnlyCollection<string> bookIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CachedBookSummary>>(
            Books.Where(book => bookIds.Contains(book.BookId, StringComparer.Ordinal)).ToArray());

    public Task<CachedBookSummary?> GetCachedBookAsync(
        string bookId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Books.FirstOrDefault(book => string.Equals(book.BookId, bookId, StringComparison.Ordinal)));

    public Task<IReadOnlyList<CachedChapterCatalogEntry>> GetCachedChapterCatalogAsync(
        string bookId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CachedChapterCatalogEntry>>(
            GetChapters(bookId)
                .Select(static chapter => new CachedChapterCatalogEntry(
                    chapter.BookId,
                    chapter.ChapterIndex,
                    chapter.Title))
                .ToArray());

    public Task<IReadOnlyList<CachedChapterSummary>> GetCachedChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CachedChapterSummary>>(
            GetChapters(bookId)
                .Where(chapter => chapterIndices.Contains(chapter.ChapterIndex))
                .ToArray());

    public Task<CachedChapterSummary?> GetCachedChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken) =>
        Task.FromResult(GetChapters(bookId).FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex));

    private IReadOnlyList<CachedChapterSummary> GetChapters(string bookId) =>
        ChaptersByBook.GetValueOrDefault(bookId, []);
}
