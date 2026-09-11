using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;

namespace NovelSpeaker.TestKit.Cache;

/// <summary>
/// Small contract-level cache double shared by presentation and WPF tests.
/// It exposes application read models and the invalidation boundary without recreating the
/// deleted cache workspace façade in each test fixture.
/// </summary>
internal sealed class CachePresentationTestDouble :
    IAudioCacheStore,
    ICacheCatalog,
    ICacheCoverageQuery,
    ICacheInvalidationCoordinator,
    ICachePlanRepairRequestor
{
    private EventHandler<CacheInvalidationBatch>? _batchPublished;
    private readonly Queue<AudioCacheStoreSummary> _summaryQueue = new();

    public AudioCacheStoreSummary StoreSummary { get; set; } = new(0, 0, 0, false);

    public IReadOnlyList<AudioCacheStoreSummary>? SummarySequence
    {
        set
        {
            _summaryQueue.Clear();
            if (value is null)
            {
                return;
            }

            foreach (var summary in value)
            {
                _summaryQueue.Enqueue(summary);
            }
        }
    }

    public Queue<TaskCompletionSource<AudioCacheStoreSummary>> PendingSummaryTasks { get; } = new();

    public TaskCompletionSource SummaryLoadStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<CachedBookSummary> Books { get; set; } = [];

    public Dictionary<string, IReadOnlyList<CachedChapterSummary>> ChaptersByBook { get; } =
        new(StringComparer.Ordinal);

    public IReadOnlyList<ChapterCacheStatus> Statuses { get; set; } = [];

    public Func<
        string,
        IReadOnlyCollection<int>,
        CancellationToken,
        Task<IReadOnlyList<ChapterCacheStatus>>>?
        CoverageHandler
    {
        get; set;
    }

    public AudioCacheStoreCleanupResult CleanupResult { get; set; } = new(0, 0, 0, 0);

    public int ClearBookCallCount { get; private set; }

    public int ClearChaptersCallCount { get; private set; }

    public (string BookId, int[] ChapterIndices)? LastClearChaptersRequest { get; private set; }

    public int CoverageQueryCallCount { get; private set; }

    public int SummaryQueryCallCount { get; private set; }

    public int ClearAllCallCount { get; private set; }

    public bool MaintenanceCalled { get; private set; }

    public int StatusCallCount => CoverageQueryCallCount;

    public int RepairRequestCount { get; private set; }

    public int InvalidationSubscriptionCount => _batchPublished?.GetInvocationList().Length ?? 0;

    public int SubscriberCount => InvalidationSubscriptionCount;

    public IReadOnlyList<int> LastRequestedChapterIndices { get; private set; } = [];

    public event EventHandler<CacheInvalidationBatch>? BatchPublished
    {
        add => _batchPublished += value;
        remove => _batchPublished -= value;
    }

    public Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        SummaryQueryCallCount++;
        if (PendingSummaryTasks.Count > 0)
        {
            SummaryLoadStarted.TrySetResult();
            return WaitForSummaryAsync(PendingSummaryTasks.Dequeue(), cancellationToken);
        }

        if (_summaryQueue.Count > 0)
        {
            StoreSummary = _summaryQueue.Dequeue();
        }

        return Task.FromResult(StoreSummary);
    }

    public Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CachedBookStoreSummary>>(
            Books.Select(static book => new CachedBookStoreSummary(
                book.BookId,
                book.ChapterCount,
                book.EntryCount,
                book.TotalSizeBytes)).ToArray());

    public Task<CachedBookStoreSummary?> GetBookAsync(
        string bookId,
        CancellationToken cancellationToken) =>
        Task.FromResult<CachedBookStoreSummary?>(
            Books.FirstOrDefault(book => string.Equals(book.BookId, bookId, StringComparison.Ordinal)) is { } book
                ? new CachedBookStoreSummary(
                    book.BookId,
                    book.ChapterCount,
                    book.EntryCount,
                    book.TotalSizeBytes)
                : null);

    public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
        string bookId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CachedChapterStoreSummary>>(
            GetChapters(bookId)
                .Select(static chapter => new CachedChapterStoreSummary(
                    chapter.BookId,
                    chapter.ChapterIndex,
                    chapter.DistinctSegmentCount,
                    chapter.EntryCount,
                    chapter.TotalSizeBytes))
                .ToArray());

    public Task<CachedChapterStoreSummary?> GetChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken) =>
        Task.FromResult<CachedChapterStoreSummary?>(
            GetChapters(bookId).FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex) is { } chapter
                ? new CachedChapterStoreSummary(
                    chapter.BookId,
                    chapter.ChapterIndex,
                    chapter.DistinctSegmentCount,
                    chapter.EntryCount,
                    chapter.TotalSizeBytes)
                : null);

    public Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(
        IReadOnlyCollection<CurrentCacheChapterQuery> chapters,
        SynthesisProfileFingerprint synthesisProfile,
        CancellationToken cancellationToken) =>
        Task.FromResult(Statuses);

    public Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesAsync(
        IReadOnlyCollection<AudioCacheKey> keys,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<AudioCacheKey>>(new HashSet<AudioCacheKey>());

    public Task<AudioCacheStoreCleanupResult> ClearChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        ClearChaptersCallCount++;
        LastClearChaptersRequest = (bookId, [chapterIndex]);
        Publish(CacheInvalidation.ForChapters(
            bookId,
            [chapterIndex],
            CacheInvalidationAspect.PhysicalSummary |
            CacheInvalidationAspect.CatalogStructure |
            CacheInvalidationAspect.Coverage));
        return Task.FromResult(CleanupResult);
    }

    public Task<AudioCacheStoreCleanupResult> ClearChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ClearChaptersCallCount++;
        LastClearChaptersRequest = (bookId, chapterIndices.ToArray());
        Publish(CacheInvalidation.ForChapters(
            bookId,
            chapterIndices,
            CacheInvalidationAspect.PhysicalSummary |
            CacheInvalidationAspect.CatalogStructure |
            CacheInvalidationAspect.Coverage));
        return Task.FromResult(CleanupResult);
    }

    public Task<AudioCacheStoreCleanupResult> ClearBookAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ClearBookCallCount++;
        Publish(CacheInvalidation.ForBook(
            bookId,
            CacheInvalidationAspect.PhysicalSummary |
            CacheInvalidationAspect.CatalogStructure |
            CacheInvalidationAspect.Coverage));
        return Task.FromResult(CleanupResult);
    }

    public Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
    {
        ClearAllCallCount++;
        Publish(CacheInvalidation.ForGlobal(
            CacheInvalidationAspect.PhysicalSummary |
            CacheInvalidationAspect.CatalogStructure |
            CacheInvalidationAspect.Coverage));
        return Task.FromResult(CleanupResult);
    }

    public Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        MaintenanceCalled = true;
        Publish(CacheInvalidation.ForGlobal(
            CacheInvalidationAspect.PhysicalSummary |
            CacheInvalidationAspect.CatalogStructure |
            CacheInvalidationAspect.Coverage));
        return Task.CompletedTask;
    }

    public Task RunStartupMaintenanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var summary = await GetSummaryAsync(cancellationToken);
        return new CacheOverviewModel(
            summary.TotalSizeBytes,
            summary.EntryCount,
            summary.LimitBytes,
            summary.IsOverLimit);
    }

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
        Task.FromResult<CachedChapterSummary?>(
            GetChapters(bookId).FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex));

    public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        CoverageQueryCallCount++;
        LastRequestedChapterIndices = chapterIndices.ToArray();
        if (CoverageHandler is not null)
        {
            return CoverageHandler(bookId, chapterIndices, cancellationToken);
        }

        return Task.FromResult<IReadOnlyList<ChapterCacheStatus>>(
            Statuses.Where(status => chapterIndices.Contains(status.ChapterIndex)).ToArray());
    }

    public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        IReadOnlyCollection<PlaybackChapterMetadata> chapters,
        CancellationToken cancellationToken) =>
        GetAsync(bookId, chapterIndices, cancellationToken);

    public Task RequestAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        IReadOnlyCollection<ChapterCacheStatus> statuses,
        CancellationToken cancellationToken)
    {
        RepairRequestCount++;
        return Task.CompletedTask;
    }

    public void Publish(CacheInvalidation invalidation) =>
        _batchPublished?.Invoke(this, new CacheInvalidationBatch([invalidation]));

    public Task FlushPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<AudioCacheStoreSummary> WaitForSummaryAsync(
        TaskCompletionSource<AudioCacheStoreSummary> pendingSummary,
        CancellationToken cancellationToken) =>
        await pendingSummary.Task.WaitAsync(cancellationToken);

    private IReadOnlyList<CachedChapterSummary> GetChapters(string bookId) =>
        ChaptersByBook.GetValueOrDefault(bookId, []);
}
