using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Composes physical cache totals with metadata and persisted current-plan coverage queries.
/// </summary>
public sealed class CacheWorkspaceService :
    ICacheWorkspaceService,
    ICacheWorkspaceBackgroundTaskOwner,
    IDisposable
{
    private readonly IAudioCacheStore _cacheStore;
    private readonly IBookPlaybackMetadataQuery _bookMetadataQuery;
    private readonly IBookLibraryQuery? _bookLibraryQuery;
    private readonly ICacheWorkspaceFailureReporter? _failureReporter;
    private readonly ICacheCoverageQuery _coverageQuery;
    private readonly ISpeechPlanRepairCoordinator _repairCoordinator;
    private readonly ICacheInvalidationCoordinator _invalidationCoordinator;
    private readonly bool _ownsRepairCoordinator;
    private readonly bool _ownsInvalidationCoordinator;
    private int _disposed;

    public CacheWorkspaceService(
        IAudioCacheStore cacheStore,
        IBookPlaybackMetadataQuery bookMetadataQuery,
        ISelectedTtsRuleProvider selectedRuleProvider,
        IAppSettingsService settingsService,
        ICacheWorkspaceFailureReporter? failureReporter = null,
        IBookPlaybackContentService? bookContentService = null,
        IRegexReplacementRuleRepository? regexRuleRepository = null,
        IChapterSpeechPlanStore? speechPlanStore = null,
        IBookLibraryQuery? bookLibraryQuery = null,
        ICacheCoverageQuery? coverageQuery = null,
        ISpeechPlanRepairCoordinator? repairCoordinator = null,
        ICacheInvalidationCoordinator? invalidationCoordinator = null)
    {
        _cacheStore = cacheStore;
        _bookMetadataQuery = bookMetadataQuery;
        _bookLibraryQuery = bookLibraryQuery;
        _failureReporter = failureReporter;
        _invalidationCoordinator = invalidationCoordinator ?? new CacheInvalidationCoordinator();
        _ownsInvalidationCoordinator = invalidationCoordinator is null;
        _coverageQuery = coverageQuery ?? new CacheCoverageQuery(
            cacheStore,
            bookMetadataQuery,
            selectedRuleProvider,
            settingsService,
            regexRuleRepository,
            failureReporter);
        _repairCoordinator = repairCoordinator ?? new SpeechPlanRepairCoordinator(
            bookContentService,
            settingsService,
            regexRuleRepository,
            speechPlanStore,
            _invalidationCoordinator,
            failureReporter);
        _ownsRepairCoordinator = repairCoordinator is null;
        _invalidationCoordinator.BatchPublished += OnInvalidationBatchPublished;
    }

    public event EventHandler<CacheChangedEventArgs>? Changed;

    public async Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var summary = await _cacheStore.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
        return new CacheOverviewModel(
            summary.TotalSizeBytes,
            summary.EntryCount,
            summary.LimitBytes,
            summary.IsOverLimit);
    }

    public async Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(
        CancellationToken cancellationToken)
    {
        var summaries = await _cacheStore.GetBooksAsync(cancellationToken).ConfigureAwait(false);
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
        var items = new List<CachedBookCacheItem>(summaries.Count);
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

            items.Add(new CachedBookCacheItem(
                summary.BookId,
                metadata?.Title ?? summary.BookId,
                metadata?.Author,
                summary.ChapterCount,
                summary.EntryCount,
                summary.TotalSizeBytes));
        }

        return items;
    }

    public async Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        var summaries = await _cacheStore.GetChaptersAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (summaries.Count == 0)
        {
            return [];
        }

        var chapterIndices = summaries.Select(summary => summary.ChapterIndex).ToArray();
        var coverage = await GetCoverageDataAsync(bookId, chapterIndices, cancellationToken)
            .ConfigureAwait(false);
        var chapters = coverage.Chapters;
        var statusesByIndex = coverage.Statuses;
        var titlesByIndex = chapters.ToDictionary(
            static chapter => chapter.ChapterIndex,
            static chapter => chapter.Title);
        QueuePlanRepairs(bookId, chapters, statusesByIndex, includeMissing: true);

        var items = new List<CachedChapterCacheItem>(summaries.Count);
        foreach (var summary in summaries)
        {
            var status = statusesByIndex[summary.ChapterIndex];

            items.Add(new CachedChapterCacheItem(
                summary.BookId,
                summary.ChapterIndex,
                titlesByIndex.GetValueOrDefault(summary.ChapterIndex) ??
                    $"第 {summary.ChapterIndex + 1} 章",
                status.CachedSegmentCount,
                summary.EntryCount,
                summary.TotalSizeBytes,
                status.TotalSegmentCount)
            {
                CurrentConfigurationStatus = status.Kind
            });
        }

        return items;
    }

    public async Task<CachedBookCacheItem?> GetCachedBookAsync(
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
        return new CachedBookCacheItem(
            summary.BookId,
            metadata?.Title ?? summary.BookId,
            metadata?.Author,
            summary.ChapterCount,
            summary.EntryCount,
            summary.TotalSizeBytes);
    }

    public async Task<CachedChapterCacheItem?> GetCachedChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var summary = await _cacheStore
            .GetChapterAsync(bookId, chapterIndex, cancellationToken)
            .ConfigureAwait(false);
        if (summary is null)
        {
            return null;
        }

        var coverage = await GetCoverageDataAsync(bookId, [chapterIndex], cancellationToken)
            .ConfigureAwait(false);
        var chapters = coverage.Chapters;
        var status = coverage.Statuses[chapterIndex];
        var title = chapters.FirstOrDefault()?.Title;
        QueuePlanRepairs(
            bookId,
            chapters,
            new Dictionary<int, ChapterCacheStatus> { [chapterIndex] = status },
            includeMissing: true);
        return new CachedChapterCacheItem(
            summary.BookId,
            summary.ChapterIndex,
            title ?? $"第 {summary.ChapterIndex + 1} 章",
            status.CachedSegmentCount,
            summary.EntryCount,
            summary.TotalSizeBytes,
            status.TotalSegmentCount)
        {
            CurrentConfigurationStatus = status.Kind
        };
    }

    public async Task<IReadOnlyList<CachedChapterCatalogEntry>> GetCachedChapterCatalogAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        var summaries = await _cacheStore.GetChaptersAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (summaries.Count == 0)
        {
            return [];
        }

        var chapters = await _bookMetadataQuery.GetChaptersAsync(
            bookId,
            summaries.Select(static summary => summary.ChapterIndex).ToArray(),
            cancellationToken).ConfigureAwait(false);
        var titlesByIndex = chapters.ToDictionary(
            static chapter => chapter.ChapterIndex,
            static chapter => chapter.Title);

        return summaries
            .Select(summary => new CachedChapterCatalogEntry(
                summary.BookId,
                summary.ChapterIndex,
                titlesByIndex.GetValueOrDefault(
                    summary.ChapterIndex,
                    $"第 {summary.ChapterIndex + 1} 章")))
            .ToArray();
    }

    public async Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChapterDecorationsAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);

        var requested = chapterIndices.ToHashSet();
        if (requested.Count == 0)
        {
            return [];
        }

        var summaries = (await _cacheStore
                .GetChaptersAsync(bookId, requested, cancellationToken)
                .ConfigureAwait(false))
            .Where(summary => requested.Contains(summary.ChapterIndex))
            .ToArray();
        if (summaries.Length == 0)
        {
            return [];
        }

        var indices = summaries.Select(static summary => summary.ChapterIndex).ToArray();
        var coverage = await GetCoverageDataAsync(bookId, indices, cancellationToken)
            .ConfigureAwait(false);
        var chapters = coverage.Chapters;
        var statusesByIndex = coverage.Statuses;
        var titlesByIndex = chapters.ToDictionary(
            static chapter => chapter.ChapterIndex,
            static chapter => chapter.Title);
        QueuePlanRepairs(bookId, chapters, statusesByIndex, includeMissing: true);

        return summaries
            .Select(summary =>
            {
                var status = statusesByIndex[summary.ChapterIndex];
                return new CachedChapterCacheItem(
                    summary.BookId,
                    summary.ChapterIndex,
                    titlesByIndex.GetValueOrDefault(summary.ChapterIndex) ??
                        $"第 {summary.ChapterIndex + 1} 章",
                    status.CachedSegmentCount,
                    summary.EntryCount,
                    summary.TotalSizeBytes,
                    status.TotalSegmentCount)
                {
                    CurrentConfigurationStatus = status.Kind
                };
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<ChapterCacheStatus>> GetChapterCacheStatusesAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);

        var normalizedIndices = NormalizeChapterIndices(chapterIndices);
        if (normalizedIndices.Length == 0)
        {
            return [];
        }

        var coverage = await GetCoverageDataAsync(bookId, normalizedIndices, cancellationToken)
            .ConfigureAwait(false);
        var statuses = normalizedIndices.Select(index => coverage.Statuses[index]).ToArray();
        var chapters = coverage.Chapters;
        QueuePlanRepairs(
            bookId,
            chapters,
            statuses.ToDictionary(status => status.ChapterIndex),
            includeMissing: false);
        return statuses;
    }

    public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken)
    {
        return _cacheStore.RunMaintenanceAsync(cancellationToken);
    }

    public async Task<CacheCleanupResult> ClearBookAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var result = await _cacheStore.ClearBookAsync(bookId, cancellationToken).ConfigureAwait(false);
        return MapCleanupResult(result);
    }

    public async Task<CacheCleanupResult> ClearChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var result = await _cacheStore
            .ClearChapterAsync(bookId, chapterIndex, cancellationToken)
            .ConfigureAwait(false);
        return MapCleanupResult(result);
    }

    public async Task<CacheCleanupResult> ClearChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);

        var normalizedIndices = chapterIndices
            .Distinct()
            .Order()
            .ToArray();
        if (normalizedIndices.Length == 0)
        {
            throw new ArgumentException("At least one chapter must be selected.", nameof(chapterIndices));
        }

        if (normalizedIndices[0] < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chapterIndices));
        }

        var result = await _cacheStore
            .ClearChaptersAsync(bookId, normalizedIndices, cancellationToken)
            .ConfigureAwait(false);
        return MapCleanupResult(result);
    }

    public async Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
    {
        var result = await _cacheStore.ClearAllAsync(cancellationToken).ConfigureAwait(false);
        return MapCleanupResult(result);
    }

    private static int[] NormalizeChapterIndices(IReadOnlyCollection<int> chapterIndices)
    {
        var normalizedIndices = chapterIndices.Distinct().Order().ToArray();
        if (normalizedIndices.Length > 0 && normalizedIndices[0] < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chapterIndices));
        }

        return normalizedIndices;
    }

    private async Task<CoverageData> GetCoverageDataAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        try
        {
            var chapters = await _bookMetadataQuery
                .GetChaptersAsync(bookId, chapterIndices, cancellationToken)
                .ConfigureAwait(false);
            var statuses = await _coverageQuery
                .GetAsync(bookId, chapterIndices, chapters, cancellationToken)
                .ConfigureAwait(false);
            return new CoverageData(
                chapters,
                statuses.ToDictionary(status => status.ChapterIndex));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedCompletenessFailure(exception))
        {
            ReportCompletenessFailure(exception);
            return new CoverageData(
                [],
                chapterIndices.ToDictionary(
                    index => index,
                    static index => new ChapterCacheStatus(index, 0, null)
                    {
                        Kind = ChapterCacheStatusKind.ConfigurationUnavailable
                    }));
        }
    }

    private static bool IsExpectedCompletenessFailure(Exception exception) =>
        exception is FileNotFoundException or
            DirectoryNotFoundException or
            UnauthorizedAccessException or
            ArgumentOutOfRangeException or
            IOException or
            DecoderFallbackException or
            InvalidDataException;

    private void ReportCompletenessFailure(Exception exception)
    {
        try
        {
            _failureReporter?.ReportCompletenessUnavailable(exception);
        }
        catch
        {
            // Diagnostics are best effort and must not replace a read-only result.
        }
    }

    private void QueuePlanRepairs(
        string bookId,
        IReadOnlyCollection<PlaybackChapterMetadata> chapters,
        IReadOnlyDictionary<int, ChapterCacheStatus> statusesByIndex,
        bool includeMissing)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        foreach (var chapter in chapters)
        {
            if (string.IsNullOrWhiteSpace(chapter.ChapterId) ||
                !statusesByIndex.TryGetValue(chapter.ChapterIndex, out var status) ||
                (status.Kind != ChapterCacheStatusKind.PlanStale &&
                 !(includeMissing && status.Kind == ChapterCacheStatusKind.PlanMissing)))
            {
                continue;
            }

            ObserveRepair(
                _repairCoordinator.RequestAsync(
                    new SpeechPlanRepairRequest(bookId, chapter.ChapterIndex, chapter.ChapterId!),
                    CancellationToken.None));
        }
    }

    private void OnInvalidationBatchPublished(object? sender, CacheInvalidationBatch batch)
    {
        foreach (var change in batch.Changes)
        {
            switch (change.Scope)
            {
                case CacheInvalidationScope.Global:
                    Changed?.Invoke(this, new CacheChangedEventArgs(null, null));
                    break;
                case CacheInvalidationScope.Book book:
                    Changed?.Invoke(this, new CacheChangedEventArgs(book.BookId, null));
                    break;
                case CacheInvalidationScope.Chapters chapters:
                    foreach (var chapterIndex in chapters.ChapterIndices)
                    {
                        Changed?.Invoke(this, new CacheChangedEventArgs(chapters.BookId, chapterIndex));
                    }
                    break;
            }
        }
    }

    private static void ObserveRepair(Task repair)
    {
        _ = repair.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public Task StopBackgroundOperationsAsync(CancellationToken cancellationToken) =>
        _repairCoordinator.StopAsync(cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _invalidationCoordinator.BatchPublished -= OnInvalidationBatchPublished;
        if (_ownsRepairCoordinator)
        {
            _repairCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_ownsInvalidationCoordinator)
        {
            _invalidationCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static CacheCleanupResult MapCleanupResult(AudioCacheStoreCleanupResult result)
    {
        return new CacheCleanupResult(
            result.DeletedBytes,
            result.DeletedEntryCount,
            result.ProtectedEntryCount,
            result.FailedEntryCount);
    }

    private sealed record CoverageData(
        IReadOnlyList<PlaybackChapterMetadata> Chapters,
        IReadOnlyDictionary<int, ChapterCacheStatus> Statuses);
}
