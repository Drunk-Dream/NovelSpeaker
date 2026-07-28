using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Compilation;

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
        var defaultSpeakSpeed = _settingsService.Current.DefaultSpeakSpeed;
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
                defaultSpeakSpeed,
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
                status.TotalSegmentCount));
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
                .Select(ChapterCacheStatusUnavailable)
                .ToArray();
        }

        var data = await TryGetCurrentConfigurationDataAsync(
            bookId,
            normalizedIndices,
            selectedRule.NormalizedRule,
            _settingsService.Current.DefaultSpeakSpeed,
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
        int speakSpeed,
        CancellationToken cancellationToken)
    {
        try
        {
            var chapters = await _bookContentService
                .GetChaptersAsync(bookId, chapterIndices, cancellationToken)
                .ConfigureAwait(false);
            var chaptersByIndex = chapters.ToDictionary(chapter => chapter.ChapterIndex);
            var synthesisProfile = SynthesisProfileFingerprint.Create(
                TtsRuleFingerprint.Create(normalizedRule),
                speakSpeed);
            var keysByChapter = new Dictionary<int, AudioCacheKey[]>(chapterIndices.Count);
            foreach (var chapterIndex in chapterIndices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!chaptersByIndex.TryGetValue(chapterIndex, out var chapter))
                {
                    continue;
                }

                keysByChapter.Add(
                    chapterIndex,
                    chapter.Segments
                        .Where(segment => !string.IsNullOrWhiteSpace(segment.SpeechText))
                        .Select(segment => AudioCacheKey.FromIdentity(AudioCacheIdentity.Create(
                            chapter.ChapterId ?? $"{bookId}/chapter/{chapterIndex}",
                            segment.StableIdentity,
                            segment.SpeechText,
                            synthesisProfile)))
                        .ToArray());
            }

            cancellationToken.ThrowIfCancellationRequested();
            var allKeys = keysByChapter.Values.SelectMany(keys => keys).ToArray();
            IReadOnlySet<AudioCacheKey> validEntries = new HashSet<AudioCacheKey>();
            if (allKeys.Length > 0)
            {
                validEntries = await _cacheStore
                    .GetValidEntriesAsync(allKeys, cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var statuses = new Dictionary<int, ChapterCacheStatus>(chapterIndices.Count);
            foreach (var chapterIndex in chapterIndices)
            {
                if (!keysByChapter.TryGetValue(chapterIndex, out var keys))
                {
                    statuses.Add(chapterIndex, ChapterCacheStatusUnavailable(chapterIndex));
                    continue;
                }

                statuses.Add(
                    chapterIndex,
                    new ChapterCacheStatus(
                        chapterIndex,
                        keys.Count(validEntries.Contains),
                        keys.Length));
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
            chapterIndices.ToDictionary(index => index, ChapterCacheStatusUnavailable),
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

    private static ChapterCacheStatus ChapterCacheStatusUnavailable(int chapterIndex) =>
        new(chapterIndex, 0, null);

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
                chapterIndices.ToDictionary(index => index, ChapterCacheStatusUnavailable),
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
