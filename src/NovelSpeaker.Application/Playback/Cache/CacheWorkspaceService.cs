using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Composes cache storage totals with Books/Text queries for cache management callers.
/// </summary>
public sealed class CacheWorkspaceService : ICacheWorkspaceService
{
    private readonly IAudioCacheStore _cacheStore;
    private readonly IBookPlaybackMetadataQuery _bookMetadataQuery;
    private readonly IBookPlaybackContentService _bookContentService;
    private readonly ISelectedTtsRuleProvider _selectedRuleProvider;
    private readonly IAppSettingsService _settingsService;
    private readonly ICacheWorkspaceFailureReporter? _failureReporter;

    public CacheWorkspaceService(
        IAudioCacheStore cacheStore,
        IBookPlaybackMetadataQuery bookMetadataQuery,
        IBookPlaybackContentService bookContentService,
        ISelectedTtsRuleProvider selectedRuleProvider,
        IAppSettingsService settingsService,
        ICacheWorkspaceFailureReporter? failureReporter = null)
    {
        _cacheStore = cacheStore;
        _bookMetadataQuery = bookMetadataQuery;
        _bookContentService = bookContentService;
        _selectedRuleProvider = selectedRuleProvider;
        _settingsService = settingsService;
        _failureReporter = failureReporter;
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
        var defaultSpeakSpeed = _settingsService.Current.DefaultSpeakSpeed;
        var items = new List<CachedChapterCacheItem>(summaries.Count);
        foreach (var summary in summaries)
        {
            var metadata = await _bookMetadataQuery
                .GetChapterAsync(bookId, summary.ChapterIndex, cancellationToken)
                .ConfigureAwait(false);
            var completeness = selectedRule is null || metadata is null
                ? CurrentConfigurationCompleteness.Unavailable
                : await TryGetCurrentConfigurationCompletenessAsync(
                    summary.BookId,
                    summary.ChapterIndex,
                    selectedRule.RuleId,
                    defaultSpeakSpeed,
                    cancellationToken).ConfigureAwait(false);

            items.Add(new CachedChapterCacheItem(
                summary.BookId,
                summary.ChapterIndex,
                metadata?.Title ?? $"第 {summary.ChapterIndex + 1} 章",
                completeness.CachedSegmentCount,
                summary.EntryCount,
                summary.TotalSizeBytes,
                completeness.TotalSegmentCount));
        }

        return items;
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

    private async Task<CurrentConfigurationCompleteness> TryGetCurrentConfigurationCompletenessAsync(
        string bookId,
        int chapterIndex,
        long ruleId,
        int speakSpeed,
        CancellationToken cancellationToken)
    {
        try
        {
            var chapter = await _bookContentService
                .GetChapterAsync(bookId, chapterIndex, cancellationToken)
                .ConfigureAwait(false);
            if (chapter is null)
            {
                return CurrentConfigurationCompleteness.Unavailable;
            }

            var keys = chapter.Segments
                .Where(segment => !string.IsNullOrWhiteSpace(segment.SpeechText))
                .Select(segment => AudioCacheKey.FromPlayback(
                    bookId,
                    chapterIndex,
                    segment.SegmentIndex,
                    ruleId,
                    speakSpeed,
                    segment.SpeechText))
                .ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            if (keys.Length == 0)
            {
                return new CurrentConfigurationCompleteness(0, 0);
            }

            var validEntries = await _cacheStore
                .GetValidEntriesAsync(keys, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new CurrentConfigurationCompleteness(validEntries.Count, keys.Length);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedCompletenessFailure(exception))
        {
            _failureReporter?.ReportCompletenessUnavailable(exception);
            return CurrentConfigurationCompleteness.Unavailable;
        }
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

    private readonly record struct CurrentConfigurationCompleteness(
        int CachedSegmentCount,
        int? TotalSegmentCount)
    {
        public static CurrentConfigurationCompleteness Unavailable { get; } = new(0, null);
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
