using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Composes cache storage totals with Books/Text queries for cache management callers.
/// </summary>
public sealed class CacheWorkspaceService : ICacheWorkspaceService
{
    private readonly IAudioCacheStore _cacheStore;
    private readonly IBookPlaybackMetadataQuery _bookMetadataQuery;
    private readonly IBookContentReader _bookContentReader;
    private readonly ITextSegmenter _textSegmenter;
    private readonly ITextSegmentationOptionsProvider _optionsProvider;
    private readonly ICacheWorkspaceFailureReporter? _failureReporter;

    public CacheWorkspaceService(
        IAudioCacheStore cacheStore,
        IBookPlaybackMetadataQuery bookMetadataQuery,
        IBookContentReader bookContentReader,
        ITextSegmenter textSegmenter,
        ITextSegmentationOptionsProvider optionsProvider,
        ICacheWorkspaceFailureReporter? failureReporter = null)
    {
        _cacheStore = cacheStore;
        _bookMetadataQuery = bookMetadataQuery;
        _bookContentReader = bookContentReader;
        _textSegmenter = textSegmenter;
        _optionsProvider = optionsProvider;
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

        var options = _optionsProvider.GetCurrent();
        var items = new List<CachedChapterCacheItem>(summaries.Count);
        foreach (var summary in summaries)
        {
            var metadata = await _bookMetadataQuery
                .GetChapterAsync(bookId, summary.ChapterIndex, cancellationToken)
                .ConfigureAwait(false);
            var estimatedTotalSegmentCount = metadata is null
                ? null
                : await TryEstimateSegmentCountAsync(metadata, options, cancellationToken).ConfigureAwait(false);

            items.Add(new CachedChapterCacheItem(
                summary.BookId,
                summary.ChapterIndex,
                metadata?.Title ?? $"第 {summary.ChapterIndex + 1} 章",
                summary.DistinctSegmentCount,
                summary.EntryCount,
                summary.TotalSizeBytes,
                estimatedTotalSegmentCount));
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

    public async Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
    {
        var result = await _cacheStore.ClearAllAsync(cancellationToken).ConfigureAwait(false);
        return MapCleanupResult(result);
    }

    private async Task<int?> TryEstimateSegmentCountAsync(
        PlaybackChapterMetadata metadata,
        TextSegmentationOptions options,
        CancellationToken cancellationToken)
    {
        if (metadata.StartOffset < 0 || metadata.Length <= 0)
        {
            return null;
        }

        try
        {
            var text = await _bookContentReader.ReadChapterTextAsync(
                metadata.StoredFilePath,
                metadata.StartOffset,
                metadata.Length,
                cancellationToken).ConfigureAwait(false);

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _textSegmenter.Segment(text, options).Count;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedEstimationFailure(exception))
        {
            _failureReporter?.ReportEstimationFallback(exception);
            return null;
        }
    }

    private static bool IsExpectedEstimationFailure(Exception exception)
    {
        return exception is FileNotFoundException or
            DirectoryNotFoundException or
            UnauthorizedAccessException or
            IOException or
            DecoderFallbackException or
            InvalidDataException;
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
