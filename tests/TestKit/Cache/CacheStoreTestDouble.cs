using NovelSpeaker.Application.Cache;

namespace NovelSpeaker.TestKit.Cache;

internal sealed class CacheStoreTestDouble : IAudioCacheStore
{
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

    public AudioCacheStoreCleanupResult CleanupResult { get; set; } = new(0, 0, 0, 0);

    public int ClearBookCallCount { get; private set; }

    public int ClearChaptersCallCount { get; private set; }

    public (string BookId, int[] ChapterIndices)? LastClearChaptersRequest { get; private set; }

    public int SummaryQueryCallCount { get; private set; }

    public bool MaintenanceCalled { get; private set; }

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

    public async Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var summary = await GetSummaryAsync(cancellationToken);
        return new CacheOverviewModel(
            summary.TotalSizeBytes,
            summary.EntryCount,
            summary.LimitBytes,
            summary.IsOverLimit);
    }

    public Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CachedBookStoreSummary>>([]);

    public Task<CachedBookStoreSummary?> GetBookAsync(
        string bookId,
        CancellationToken cancellationToken) =>
        Task.FromResult<CachedBookStoreSummary?>(null);

    public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
        string bookId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CachedChapterStoreSummary>>([]);

    public Task<CachedChapterStoreSummary?> GetChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken) =>
        Task.FromResult<CachedChapterStoreSummary?>(null);

    public Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(
        IReadOnlyCollection<CurrentCacheChapterQuery> chapters,
        SynthesisProfileFingerprint synthesisProfile,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ChapterCacheStatus>>([]);

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
        return Task.FromResult(CleanupResult);
    }

    public Task<AudioCacheStoreCleanupResult> ClearChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ClearChaptersCallCount++;
        LastClearChaptersRequest = (bookId, chapterIndices.ToArray());
        return Task.FromResult(CleanupResult);
    }

    public Task<AudioCacheStoreCleanupResult> ClearBookAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ClearBookCallCount++;
        return Task.FromResult(CleanupResult);
    }

    public Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
    {
        ClearAllCallCount++;
        return Task.FromResult(CleanupResult);
    }

    public int ClearAllCallCount { get; private set; }

    public Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        MaintenanceCalled = true;
        return Task.CompletedTask;
    }

    public Task RunStartupMaintenanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<AudioCacheStoreSummary> WaitForSummaryAsync(
        TaskCompletionSource<AudioCacheStoreSummary> pendingSummary,
        CancellationToken cancellationToken) =>
        await pendingSummary.Task.WaitAsync(cancellationToken);
}
