using NovelSpeaker.Application.Cache;

namespace NovelSpeaker.TestKit.Cache;

internal sealed class CacheReadModelTestDouble : ICacheReadModel
{
    private EventHandler<CacheReadModelChange>? _changed;
    public long Revision { get; private set; }
    public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;
    public event EventHandler<CacheReadModelChange>? Changed { add => _changed += value; remove => _changed -= value; }
    public IReadOnlyList<CachedBookSummary> Books { get; set; } = [];
    public Dictionary<string, IReadOnlyList<CacheChapterView>> ChaptersByBook { get; } = new(StringComparer.Ordinal);
    public List<(string BookId, IReadOnlyCollection<int> Indices)> ChapterQueries { get; } = [];
    public Func<string, IReadOnlyCollection<int>, CancellationToken, Task<CacheReadResult<IReadOnlyList<CacheChapterView>>>>? ChapterHandler { get; set; }
    public Func<string, CancellationToken, Task<CacheReadResult<CachedBookCatalog>>>? BookHandler { get; set; }
    public Func<CancellationToken, Task<CacheOverviewModel>>? OverviewHandler { get; set; }
    public IReadOnlyList<ChapterCacheStatus> Statuses { get; set; } = [];
    public Func<string, IReadOnlyCollection<int>, CancellationToken, Task<IReadOnlyList<ChapterCacheStatus>>>? CoverageHandler { get; set; }
    public int StatusCallCount => ChapterQueries.Count;
    public IReadOnlyList<int> LastRequestedChapterIndices => ChapterQueries.LastOrDefault().Indices?.ToArray() ?? [];

    public void Publish(params CacheReadModelScope[] scopes)
    {
        Revision++;
        _changed?.Invoke(this, new CacheReadModelChange(Revision, scopes));
    }

    public void PublishOverviewChange(params CacheReadModelScope[] scopes)
    {
        Revision++;
        _changed?.Invoke(this, new CacheReadModelChange(Revision, scopes, OverviewChanged: true));
    }

    public async Task<CacheReadResult<CacheOverviewModel>> GetOverviewAsync(CancellationToken cancellationToken) =>
        new(Revision, OverviewHandler is not null ? await OverviewHandler(cancellationToken) :
            new(Books.Sum(book => book.TotalSizeBytes), Books.Sum(book => book.EntryCount), 0, false));

    public Task<CacheReadResult<IReadOnlyList<CachedBookSummary>>> GetBooksAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new CacheReadResult<IReadOnlyList<CachedBookSummary>>(Revision, Books));

    public Task<CacheReadResult<IReadOnlyList<CachedBookSummary>>> GetBooksAsync(IReadOnlyCollection<string> bookIds, CancellationToken cancellationToken) =>
        Task.FromResult(new CacheReadResult<IReadOnlyList<CachedBookSummary>>(Revision, Books.Where(book => bookIds.Contains(book.BookId)).ToArray()));

    public Task<CacheReadResult<CachedBookCatalog>> GetBookAsync(string bookId, CancellationToken cancellationToken) =>
        BookHandler?.Invoke(bookId, cancellationToken) ?? Task.FromResult(new CacheReadResult<CachedBookCatalog>(Revision,
            new(Books.FirstOrDefault(book => book.BookId == bookId), ChaptersByBook.GetValueOrDefault(bookId, [])
                .Where(static view => view.Physical is not null)
                .Select(view => new CachedChapterCatalogEntry(bookId, view.ChapterIndex, view.Physical!.Title)).ToArray())));

    public async Task<CacheReadResult<IReadOnlyList<CacheChapterView>>> GetChaptersAsync(string bookId, IReadOnlyCollection<int> chapterIndices, CancellationToken cancellationToken)
    {
        ChapterQueries.Add((bookId, chapterIndices.ToArray()));
        if (ChapterHandler is not null)
        {
            return await ChapterHandler(bookId, chapterIndices, cancellationToken);
        }

        var statuses = CoverageHandler is not null ? await CoverageHandler(bookId, chapterIndices, cancellationToken) : Statuses;
        return new CacheReadResult<IReadOnlyList<CacheChapterView>>(Revision, chapterIndices.Select(index =>
                ChaptersByBook.GetValueOrDefault(bookId, []).FirstOrDefault(view => view.ChapterIndex == index) ??
                new CacheChapterView(index, null, statuses.FirstOrDefault(status => status.ChapterIndex == index) ?? new(index, 0, null))).ToArray());
    }
}
