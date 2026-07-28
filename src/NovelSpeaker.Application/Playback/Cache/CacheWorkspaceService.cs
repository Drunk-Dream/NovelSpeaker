using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Composes physical cache totals with metadata and persisted current-plan coverage queries.
/// </summary>
public sealed class CacheWorkspaceService : ICacheWorkspaceService
{
    private readonly IAudioCacheStore _cacheStore;
    private readonly IBookPlaybackMetadataQuery _bookMetadataQuery;
    private readonly ISelectedTtsRuleProvider _selectedRuleProvider;
    private readonly IAppSettingsService _settingsService;
    private readonly ICacheWorkspaceFailureReporter? _failureReporter;

    public CacheWorkspaceService(
        IAudioCacheStore cacheStore,
        IBookPlaybackMetadataQuery bookMetadataQuery,
        ISelectedTtsRuleProvider selectedRuleProvider,
        IAppSettingsService settingsService,
        ICacheWorkspaceFailureReporter? failureReporter = null)
    {
        _cacheStore = cacheStore;
        _bookMetadataQuery = bookMetadataQuery;
        _selectedRuleProvider = selectedRuleProvider;
        _settingsService = settingsService;
        _failureReporter = failureReporter;
        _cacheStore.Changed += OnCacheStoreChanged;
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

        var items = new List<CachedBookCacheItem>(summaries.Count);
        foreach (var summary in summaries)
        {
            var metadata = await _bookMetadataQuery
                .GetBookAsync(summary.BookId, cancellationToken)
                .ConfigureAwait(false);
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

        var selectedRule = await _selectedRuleProvider
            .GetSelectedRuleAsync(cancellationToken)
            .ConfigureAwait(false);
        var settings = _settingsService.Current;
        var chapterIndices = summaries.Select(summary => summary.ChapterIndex).ToArray();
        CurrentConfigurationData configurationData;
        if (selectedRule is null)
        {
            configurationData = await GetUnavailableConfigurationDataAsync(
                bookId,
                chapterIndices,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            configurationData = await TryGetCurrentConfigurationDataAsync(
                bookId,
                chapterIndices,
                selectedRule.NormalizedRule,
                settings,
                cancellationToken).ConfigureAwait(false);
        }

        var items = new List<CachedChapterCacheItem>(summaries.Count);
        foreach (var summary in summaries)
        {
            var status = configurationData.Statuses[summary.ChapterIndex];

            items.Add(new CachedChapterCacheItem(
                summary.BookId,
                summary.ChapterIndex,
                configurationData.Titles.GetValueOrDefault(
                    summary.ChapterIndex,
                    $"第 {summary.ChapterIndex + 1} 章"),
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

        var selectedRule = await _selectedRuleProvider
            .GetSelectedRuleAsync(cancellationToken)
            .ConfigureAwait(false);
        if (selectedRule is null)
        {
            return normalizedIndices
                .Select(ChapterCacheStatusConfigurationUnavailable)
                .ToArray();
        }

        var data = await TryGetCurrentConfigurationDataAsync(
            bookId,
            normalizedIndices,
            selectedRule.NormalizedRule,
            _settingsService.Current,
            cancellationToken).ConfigureAwait(false);
        return normalizedIndices.Select(index => data.Statuses[index]).ToArray();
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

    private async Task<CurrentConfigurationData> TryGetCurrentConfigurationDataAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        NormalizedHttpTtsRule normalizedRule,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var chapters = await _bookMetadataQuery
                .GetChaptersAsync(bookId, chapterIndices, cancellationToken)
                .ConfigureAwait(false);
            var synthesisProfile = SynthesisProfileFingerprint.Create(
                TtsRuleFingerprint.Create(normalizedRule),
                settings.DefaultSpeakSpeed);
            var coverageQueries = chapters
                .Where(chapter => !string.IsNullOrWhiteSpace(chapter.ChapterId))
                .Select(chapter => new CurrentCacheChapterQuery(
                    chapter.ChapterId!,
                    chapter.ChapterIndex,
                    settings.ReadChapterTitle,
                    settings.ReadChapterTitle && !string.IsNullOrWhiteSpace(chapter.Title)
                        ? Fingerprint.Sha256(chapter.Title)
                        : null))
                .ToArray();
            var queriedStatuses = coverageQueries.Length == 0
                ? []
                : await _cacheStore
                    .GetCurrentConfigurationStatusesAsync(
                        coverageQueries,
                        synthesisProfile,
                        cancellationToken)
                    .ConfigureAwait(false);
            var statusesByIndex = queriedStatuses.ToDictionary(status => status.ChapterIndex);
            var statuses = new Dictionary<int, ChapterCacheStatus>(chapterIndices.Count);
            foreach (var chapterIndex in chapterIndices)
            {
                statuses.Add(
                    chapterIndex,
                    statusesByIndex.GetValueOrDefault(
                        chapterIndex,
                        ChapterCacheStatusConfigurationUnavailable(chapterIndex)));
            }

            return new CurrentConfigurationData(
                statuses,
                chapters.ToDictionary(chapter => chapter.ChapterIndex, chapter => chapter.Title));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedCompletenessFailure(exception))
        {
            _failureReporter?.ReportCompletenessUnavailable(exception);
            return CurrentConfigurationData.Unavailable(chapterIndices);
        }
    }

    private async Task<CurrentConfigurationData> GetUnavailableConfigurationDataAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        var metadata = await _bookMetadataQuery.GetBookAsync(bookId, cancellationToken).ConfigureAwait(false);
        return new CurrentConfigurationData(
            chapterIndices.ToDictionary(index => index, ChapterCacheStatusConfigurationUnavailable),
            metadata?.Chapters.ToDictionary(chapter => chapter.ChapterIndex, chapter => chapter.Title) ??
                new Dictionary<int, string>());
    }

    private static bool IsExpectedCompletenessFailure(Exception exception)
    {
        return exception is FileNotFoundException or
            DirectoryNotFoundException or
            UnauthorizedAccessException or
            IOException or
            DecoderFallbackException or
            InvalidDataException;
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

    private static ChapterCacheStatus ChapterCacheStatusConfigurationUnavailable(int chapterIndex) =>
        new(chapterIndex, 0, null)
        {
            Kind = ChapterCacheStatusKind.ConfigurationUnavailable
        };

    private void OnCacheStoreChanged(object? sender, CacheChangedEventArgs eventArgs)
    {
        Changed?.Invoke(this, eventArgs);
    }

    private sealed record CurrentConfigurationData(
        IReadOnlyDictionary<int, ChapterCacheStatus> Statuses,
        IReadOnlyDictionary<int, string> Titles)
    {
        public static CurrentConfigurationData Unavailable(IReadOnlyCollection<int> chapterIndices) =>
            new(
                chapterIndices.ToDictionary(index => index, ChapterCacheStatusConfigurationUnavailable),
                new Dictionary<int, string>());
    }

    private static CacheCleanupResult MapCleanupResult(AudioCacheStoreCleanupResult result)
    {
        return new CacheCleanupResult(
            result.DeletedBytes,
            result.DeletedEntryCount,
            result.ProtectedEntryCount,
            result.FailedEntryCount);
    }
}
