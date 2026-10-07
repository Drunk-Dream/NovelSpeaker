using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Composes Cache facts within a committed revision. No process-wide chapter mirror is retained.
/// Repair tasks belong to the process coordinator; page cancellation only cancels the query.
/// </summary>
internal sealed class CacheReadModel : ICacheReadModel, IDisposable
{
    private readonly ICacheCatalog _catalog;
    private readonly ICacheCoverageQuery _coverage;
    private readonly IBookPlaybackMetadataQuery _metadata;
    private readonly ISpeechPlanRepairCoordinator _repair;
    private readonly ICacheInvalidationCoordinator _invalidation;

    public CacheReadModel(
        ICacheCatalog catalog,
        ICacheCoverageQuery coverage,
        IBookPlaybackMetadataQuery metadata,
        ISpeechPlanRepairCoordinator repair,
        ICacheInvalidationCoordinator invalidation)
    {
        _catalog = catalog;
        _coverage = coverage;
        _metadata = metadata;
        _repair = repair;
        _invalidation = invalidation;
        _invalidation.BatchPublished += OnInvalidated;
    }

    public long Revision => _invalidation.Revision;

    public event EventHandler<CacheReadModelChange>? Changed;

    public Task<CacheReadResult<CacheOverviewModel>> GetOverviewAsync(CancellationToken cancellationToken) =>
        ReadAsync(_catalog.GetOverviewAsync, cancellationToken);

    public Task<CacheReadResult<IReadOnlyList<CachedBookSummary>>> GetBooksAsync(
        CancellationToken cancellationToken) => ReadAsync(_catalog.GetCachedBooksAsync, cancellationToken);

    public Task<CacheReadResult<IReadOnlyList<CachedBookSummary>>> GetBooksAsync(
        IReadOnlyCollection<string> bookIds, CancellationToken cancellationToken) =>
        ReadAsync(token => _catalog.GetCachedBooksAsync(bookIds, token), cancellationToken);

    public Task<CacheReadResult<CachedBookCatalog>> GetBookAsync(
        string bookId, CancellationToken cancellationToken) => ReadAsync(async token =>
        {
            var summary = await _catalog.GetCachedBookAsync(bookId, token).ConfigureAwait(false);
            var chapters = await _catalog.GetCachedChapterCatalogAsync(bookId, token).ConfigureAwait(false);
            return new CachedBookCatalog(summary, chapters);
        }, cancellationToken);

    public async Task<CacheReadResult<IReadOnlyList<CacheChapterView>>> GetChaptersAsync(
        string bookId, IReadOnlyCollection<int> chapterIndices, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);
        var indices = chapterIndices.Distinct().Order().ToArray();
        if (indices.Length > 0 && indices[0] < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chapterIndices));
        }

        var result = await ReadAsync(async token =>
        {
            var physical = await _catalog.GetCachedChaptersAsync(bookId, indices, token).ConfigureAwait(false);
            var metadata = await _metadata.GetChaptersAsync(bookId, indices, token).ConfigureAwait(false);
            var coverage = await _coverage.GetAsync(bookId, indices, metadata, token).ConfigureAwait(false);
            return (Physical: physical, Metadata: metadata, Coverage: coverage);
        }, cancellationToken).ConfigureAwait(false);

        var repairIndices = result.Value.Coverage
            .Where(static status => status.Kind is ChapterCacheStatusKind.PlanMissing or ChapterCacheStatusKind.PlanStale)
            .Select(static status => status.ChapterIndex).ToHashSet();
        foreach (var chapter in result.Value.Metadata)
        {
            if (repairIndices.Contains(chapter.ChapterIndex) && !string.IsNullOrWhiteSpace(chapter.ChapterId))
            {
                Observe(_repair.RequestAsync(
                    new SpeechPlanRepairRequest(bookId, chapter.ChapterIndex, chapter.ChapterId),
                    CancellationToken.None));
            }
        }

        var physicalByIndex = result.Value.Physical.ToDictionary(static chapter => chapter.ChapterIndex);
        var coverageByIndex = result.Value.Coverage.ToDictionary(static status => status.ChapterIndex);
        IReadOnlyList<CacheChapterView> views = Array.AsReadOnly(indices.Select(index => new CacheChapterView(
            index,
            physicalByIndex.GetValueOrDefault(index),
            coverageByIndex.GetValueOrDefault(index, new ChapterCacheStatus(index, 0, null)))).ToArray());
        return new CacheReadResult<IReadOnlyList<CacheChapterView>>(result.Revision, views);
    }

    private async Task<CacheReadResult<T>> ReadAsync<T>(
        Func<CancellationToken, Task<T>> query, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var revision = Revision;
            var value = await query(cancellationToken).ConfigureAwait(false);
            if (revision == Revision)
            {
                return new CacheReadResult<T>(revision, value);
            }
        }
    }

    private void OnInvalidated(object? sender, CacheInvalidationBatch batch)
    {
        var scopes = batch.Changes.Select(static change => change.Scope switch
        {
            CacheInvalidationScope.Global => (CacheReadModelScope)new CacheReadModelScope.Global(),
            CacheInvalidationScope.Book book => new CacheReadModelScope.Book(book.BookId),
            CacheInvalidationScope.Chapters chapters => new CacheReadModelScope.Chapters(
                chapters.BookId, Array.AsReadOnly(chapters.ChapterIndices.ToArray())),
            _ => throw new InvalidOperationException("Unknown Cache scope.")
        }).Distinct().ToArray();
        var change = new CacheReadModelChange(batch.Revision, Array.AsReadOnly(scopes));
        foreach (EventHandler<CacheReadModelChange> handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                handler(this, change);
            }
            catch
            {
                // Observer failure cannot fail a committed mutation or the Cache owner.
            }
        }
    }

    private static void Observe(Task task) => _ = task.ContinueWith(
        static completed => _ = completed.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    public void Dispose() => _invalidation.BatchPublished -= OnInvalidated;
}
