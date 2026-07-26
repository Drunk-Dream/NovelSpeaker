using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Persistence.Playback;

/// <summary>
/// Owns the SQLite representation of generated audio and its cache-management queries.
/// File existence and path policy remain outside this collaborator.
/// </summary>
internal sealed class SqliteAudioCacheIndex
{
    private const int ReadyStatus = 1;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public SqliteAudioCacheIndex(
        ISqliteConnectionFactory connectionFactory,
        TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<AudioCacheIndexEntry?> FindAsync(
        AudioCacheKey key,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT CacheKey, FilePath, FileSize
            FROM AudioCacheEntries
            WHERE CacheKey = $cacheKey
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$cacheKey", key.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadEntry(reader)
            : null;
    }

    public async Task TouchAsync(
        string cacheKey,
        string storageKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE AudioCacheEntries
            SET LastAccessedAt = $lastAccessedAt,
                FilePath = $filePath
            WHERE CacheKey = $cacheKey;
            """;
        command.Parameters.AddWithValue("$cacheKey", cacheKey);
        command.Parameters.AddWithValue(
            "$lastAccessedAt",
            SqliteDateTimeMapper.Format(_timeProvider.GetUtcNow()));
        command.Parameters.AddWithValue("$filePath", storageKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAsync(
        AudioCacheWriteRequest request,
        string storageKey,
        long fileSize,
        CancellationToken cancellationToken)
    {
        var now = SqliteDateTimeMapper.Format(_timeProvider.GetUtcNow());
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AudioCacheEntries (
                CacheKey,
                BookId,
                ChapterIndex,
                SegmentIndex,
                RuleId,
                FilePath,
                ContentType,
                FileSize,
                DurationMilliseconds,
                CreatedAt,
                LastAccessedAt,
                Status)
            VALUES (
                $cacheKey,
                $bookId,
                $chapterIndex,
                $segmentIndex,
                $ruleId,
                $filePath,
                $contentType,
                $fileSize,
                $durationMilliseconds,
                $createdAt,
                $lastAccessedAt,
                $status)
            ON CONFLICT(CacheKey) DO UPDATE SET
                BookId = excluded.BookId,
                ChapterIndex = excluded.ChapterIndex,
                SegmentIndex = excluded.SegmentIndex,
                RuleId = excluded.RuleId,
                FilePath = excluded.FilePath,
                ContentType = excluded.ContentType,
                FileSize = excluded.FileSize,
                DurationMilliseconds = excluded.DurationMilliseconds,
                LastAccessedAt = excluded.LastAccessedAt,
                Status = excluded.Status;
            """;
        command.Parameters.AddWithValue("$cacheKey", request.Key.Value);
        command.Parameters.AddWithValue("$bookId", request.BookId);
        command.Parameters.AddWithValue("$chapterIndex", request.ChapterIndex);
        command.Parameters.AddWithValue("$segmentIndex", request.SegmentIndex);
        command.Parameters.AddWithValue("$ruleId", request.RuleId);
        command.Parameters.AddWithValue("$filePath", storageKey);
        command.Parameters.AddWithValue("$contentType", (object?)request.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileSize", fileSize);
        command.Parameters.AddWithValue("$durationMilliseconds", (object?)request.DurationMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$lastAccessedAt", now);
        command.Parameters.AddWithValue("$status", ReadyStatus);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string cacheKey, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM AudioCacheEntries
            WHERE CacheKey = $cacheKey;
            """;
        command.Parameters.AddWithValue("$cacheKey", cacheKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AudioCacheIndexSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(SUM(FileSize), 0), COUNT(*)
            FROM AudioCacheEntries
            WHERE Status = $status;
            """;
        command.Parameters.AddWithValue("$status", ReadyStatus);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new AudioCacheIndexSummary(reader.GetInt64(0), reader.GetInt32(1));
    }

    public async Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT BookId, COUNT(DISTINCT ChapterIndex), COUNT(*), COALESCE(SUM(FileSize), 0)
            FROM AudioCacheEntries
            WHERE Status = $status
            GROUP BY BookId
            ORDER BY COALESCE(SUM(FileSize), 0) DESC, BookId;
            """;
        command.Parameters.AddWithValue("$status", ReadyStatus);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<CachedBookStoreSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new CachedBookStoreSummary(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt64(3)));
        }

        return items;
    }

    public async Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT BookId, ChapterIndex, COUNT(DISTINCT SegmentIndex), COUNT(*), COALESCE(SUM(FileSize), 0)
            FROM AudioCacheEntries
            WHERE Status = $status AND BookId = $bookId
            GROUP BY BookId, ChapterIndex
            ORDER BY ChapterIndex;
            """;
        command.Parameters.AddWithValue("$status", ReadyStatus);
        command.Parameters.AddWithValue("$bookId", bookId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<CachedChapterStoreSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new CachedChapterStoreSummary(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt64(4)));
        }

        return items;
    }

    public Task<IReadOnlyList<AudioCacheIndexEntry>> GetEntriesAsync(
        string? bookId,
        int? chapterIndex,
        CancellationToken cancellationToken)
    {
        return ReadEntriesAsync(
            bookId,
            chapterIndex,
            "LastAccessedAt, CreatedAt, CacheKey",
            includeStatusFilter: true,
            cancellationToken);
    }

    public Task<IReadOnlyList<AudioCacheIndexEntry>> GetAllEntriesAsync(CancellationToken cancellationToken)
    {
        return ReadEntriesAsync(
            bookId: null,
            chapterIndex: null,
            "CacheKey",
            includeStatusFilter: false,
            cancellationToken);
    }

    public Task<IReadOnlyList<AudioCacheIndexEntry>> GetLruEntriesAsync(
        CancellationToken cancellationToken)
    {
        return ReadEntriesAsync(
            bookId: null,
            chapterIndex: null,
            "LastAccessedAt, CreatedAt, CacheKey",
            includeStatusFilter: true,
            cancellationToken);
    }

    private async Task<IReadOnlyList<AudioCacheIndexEntry>> ReadEntriesAsync(
        string? bookId,
        int? chapterIndex,
        string orderBy,
        bool includeStatusFilter,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        var predicates = new List<string>();
        if (includeStatusFilter)
        {
            predicates.Add("Status = $status");
            command.Parameters.AddWithValue("$status", ReadyStatus);
        }

        if (bookId is not null)
        {
            predicates.Add("BookId = $bookId");
            command.Parameters.AddWithValue("$bookId", bookId);
        }

        if (chapterIndex is not null)
        {
            predicates.Add("ChapterIndex = $chapterIndex");
            command.Parameters.AddWithValue("$chapterIndex", chapterIndex.Value);
        }

        command.CommandText =
            $"""
            SELECT CacheKey, FilePath, FileSize
            FROM AudioCacheEntries
            {(predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}")}
            ORDER BY {orderBy};
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<AudioCacheIndexEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(ReadEntry(reader));
        }

        return items;
    }

    private static AudioCacheIndexEntry ReadEntry(SqliteDataReader reader)
    {
        return new AudioCacheIndexEntry(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2));
    }
}

internal sealed record AudioCacheIndexEntry(
    string CacheKey,
    string FilePath,
    long FileSize);

internal sealed record AudioCacheIndexSummary(
    long TotalSizeBytes,
    int EntryCount);
