using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Composes cache statistics with persisted book metadata for cache management UI.
/// </summary>
public sealed class CacheWorkspaceService : ICacheWorkspaceService
{
    private readonly IAudioCacheManagementService _cacheManagementService;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IBookContentReader _bookContentReader;
    private readonly ITextSegmenter _textSegmenter;
    private readonly ITextSegmentationOptionsProvider _optionsProvider;

    public CacheWorkspaceService(
        IAudioCacheManagementService cacheManagementService,
        ISqliteConnectionFactory connectionFactory,
        IBookContentReader bookContentReader,
        ITextSegmenter textSegmenter,
        ITextSegmentationOptionsProvider optionsProvider)
    {
        _cacheManagementService = cacheManagementService;
        _connectionFactory = connectionFactory;
        _bookContentReader = bookContentReader;
        _textSegmenter = textSegmenter;
        _optionsProvider = optionsProvider;
    }

    public async Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var summary = await _cacheManagementService.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
        return new CacheOverviewModel(summary.TotalSizeBytes, summary.EntryCount, summary.LimitBytes, summary.IsOverLimit);
    }

    public async Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken)
    {
        var summaries = await _cacheManagementService.GetBooksAsync(cancellationToken).ConfigureAwait(false);
        if (summaries.Count == 0)
        {
            return [];
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var metadata = await ReadBookMetadataAsync(connection, summaries.Select(item => item.BookId), cancellationToken).ConfigureAwait(false);

        return summaries
            .Select(summary =>
            {
                var book = metadata.GetValueOrDefault(summary.BookId);
                return new CachedBookCacheItem(
                    summary.BookId,
                    book?.Title ?? summary.BookId,
                    book?.Author,
                    summary.ChapterCount,
                    summary.EntryCount,
                    summary.TotalSizeBytes);
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        var summaries = await _cacheManagementService.GetChaptersAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (summaries.Count == 0)
        {
            return [];
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var chapters = await ReadChapterMetadataAsync(connection, bookId, cancellationToken).ConfigureAwait(false);
        var options = _optionsProvider.GetCurrent();
        var items = new List<CachedChapterCacheItem>(summaries.Count);

        foreach (var summary in summaries)
        {
            chapters.TryGetValue(summary.ChapterIndex, out var metadata);
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
        return _cacheManagementService.RunMaintenanceAsync(cancellationToken);
    }

    public async Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return MapCleanupResult(await _cacheManagementService.ClearBookAsync(bookId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<CacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return MapCleanupResult(await _cacheManagementService.ClearChapterAsync(bookId, chapterIndex, cancellationToken).ConfigureAwait(false));
    }

    public async Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
    {
        return MapCleanupResult(await _cacheManagementService.ClearAllAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<Dictionary<string, BookMetadata>> ReadBookMetadataAsync(
        SqliteConnection connection,
        IEnumerable<string> bookIds,
        CancellationToken cancellationToken)
    {
        var ids = bookIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var command = connection.CreateCommand();
        var parameterNames = new List<string>(ids.Length);
        for (var index = 0; index < ids.Length; index++)
        {
            var parameterName = $"$bookId{index}";
            parameterNames.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, ids[index]);
        }

        command.CommandText =
            $"""
            SELECT Id, Title, Author
            FROM Books
            WHERE Id IN ({string.Join(", ", parameterNames)});
            """;

        var result = new Dictionary<string, BookMetadata>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result[reader.GetString(0)] = new BookMetadata(
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2));
        }

        return result;
    }

    private static async Task<Dictionary<int, ChapterMetadata>> ReadChapterMetadataAsync(
        SqliteConnection connection,
        string bookId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.ChapterIndex, c.Title, b.StoredFilePath, c.StartOffset, c.Length
            FROM Chapters c
            INNER JOIN Books b
                ON b.Id = c.BookId
            WHERE c.BookId = $bookId
            ORDER BY c.SortOrder, c.ChapterIndex;
            """;
        command.Parameters.AddWithValue("$bookId", bookId);

        var result = new Dictionary<int, ChapterMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result[reader.GetInt32(0)] = new ChapterMetadata(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4));
        }

        return result;
    }

    private async Task<int?> TryEstimateSegmentCountAsync(
        ChapterMetadata metadata,
        Domain.Books.TextSegmentationOptions options,
        CancellationToken cancellationToken)
    {
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
        catch
        {
            return null;
        }
    }

    private static CacheCleanupResult MapCleanupResult(AudioCacheCleanupResult result)
    {
        return new CacheCleanupResult(
            result.DeletedBytes,
            result.DeletedEntryCount,
            result.ProtectedEntryCount,
            result.FailedEntryCount);
    }

    private sealed record BookMetadata(string Title, string? Author);

    private sealed record ChapterMetadata(string Title, string StoredFilePath, int StartOffset, int Length);
}
