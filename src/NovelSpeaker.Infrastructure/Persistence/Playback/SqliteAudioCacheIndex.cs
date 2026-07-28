using System.Text;
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Cache;
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
    private const int ReadyHealthState = 1;
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
        command.Parameters.AddWithValue("$cacheKey", ToCacheKeyBlob(key.Value));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadEntry(reader)
            : null;
    }

    public async Task<IReadOnlyDictionary<string, AudioCacheIndexEntry>> FindManyAsync(
        IReadOnlyCollection<AudioCacheKey> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var cacheKeys = keys
            .Select(key => key.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (cacheKeys.Length == 0)
        {
            return new Dictionary<string, AudioCacheIndexEntry>(StringComparer.Ordinal);
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var entries = new Dictionary<string, AudioCacheIndexEntry>(cacheKeys.Length, StringComparer.Ordinal);
        const int batchSize = 400;
        for (var offset = 0; offset < cacheKeys.Length; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchCount = Math.Min(batchSize, cacheKeys.Length - offset);
            var command = connection.CreateCommand();
            var parameterNames = new string[batchCount];
            for (var index = 0; index < batchCount; index++)
            {
                var parameterName = $"$cacheKey{index}";
                parameterNames[index] = parameterName;
                command.Parameters.AddWithValue(
                    parameterName,
                    ToCacheKeyBlob(cacheKeys[offset + index]));
            }

            command.CommandText =
                $"""
                SELECT CacheKey, FilePath, FileSize
                FROM AudioCacheEntries
                WHERE CacheKey IN ({string.Join(", ", parameterNames)});
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var entry = ReadEntry(reader);
                entries.Add(entry.CacheKey, entry);
            }
        }

        return entries;
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
        command.Parameters.AddWithValue("$cacheKey", ToCacheKeyBlob(cacheKey));
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
        var chapterId = await ResolveChapterIdAsync(connection, request, cancellationToken).ConfigureAwait(false);
        var synthesisProfileFingerprint = Fingerprint.Sha256($"legacy-synthesis-v1:{request.RuleId}");
        await EnsureSynthesisProfileAsync(
            connection,
            synthesisProfileFingerprint,
            request.RuleId,
            now,
            cancellationToken).ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AudioCacheEntries (
                CacheKey,
                KeyVersion,
                BookId,
                ChapterId,
                SegmentKind,
                SourceStartOffset,
                SourceLength,
                SpeechTextHash,
                SynthesisProfileFingerprint,
                FilePath,
                ContentType,
                FileSize,
                DurationMilliseconds,
                HealthState,
                ValidatedAt,
                CreatedAt,
                LastAccessedAt)
            VALUES (
                $cacheKey,
                $keyVersion,
                $bookId,
                $chapterId,
                $segmentKind,
                $sourceStartOffset,
                $sourceLength,
                $speechTextHash,
                $synthesisProfileFingerprint,
                $filePath,
                $contentType,
                $fileSize,
                $durationMilliseconds,
                $healthState,
                $validatedAt,
                $createdAt,
                $lastAccessedAt)
            ON CONFLICT(CacheKey) DO UPDATE SET
                KeyVersion = excluded.KeyVersion,
                BookId = excluded.BookId,
                ChapterId = excluded.ChapterId,
                SegmentKind = excluded.SegmentKind,
                SourceStartOffset = excluded.SourceStartOffset,
                SourceLength = excluded.SourceLength,
                SpeechTextHash = excluded.SpeechTextHash,
                SynthesisProfileFingerprint = excluded.SynthesisProfileFingerprint,
                FilePath = excluded.FilePath,
                ContentType = excluded.ContentType,
                FileSize = excluded.FileSize,
                DurationMilliseconds = excluded.DurationMilliseconds,
                HealthState = excluded.HealthState,
                ValidatedAt = excluded.ValidatedAt,
                CreatedAt = excluded.CreatedAt,
                LastAccessedAt = excluded.LastAccessedAt;
            """;
        command.Parameters.AddWithValue("$cacheKey", ToCacheKeyBlob(request.Key.Value));
        command.Parameters.AddWithValue("$keyVersion", 1);
        command.Parameters.AddWithValue("$bookId", request.BookId);
        command.Parameters.AddWithValue("$chapterId", chapterId);
        command.Parameters.AddWithValue("$segmentKind", 0);
        command.Parameters.AddWithValue("$sourceStartOffset", request.SegmentIndex);
        command.Parameters.AddWithValue("$sourceLength", 1);
        command.Parameters.AddWithValue("$speechTextHash", Fingerprint.Sha256(request.Key.Value).ToArray());
        command.Parameters.AddWithValue("$synthesisProfileFingerprint", synthesisProfileFingerprint.ToArray());
        command.Parameters.AddWithValue("$filePath", storageKey);
        command.Parameters.AddWithValue("$contentType", (object?)request.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileSize", fileSize);
        command.Parameters.AddWithValue("$durationMilliseconds", (object?)request.DurationMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$healthState", ReadyHealthState);
        command.Parameters.AddWithValue("$validatedAt", now);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$lastAccessedAt", now);
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
        command.Parameters.AddWithValue("$cacheKey", ToCacheKeyBlob(cacheKey));
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
            WHERE HealthState = $status;
            """;
        command.Parameters.AddWithValue("$status", ReadyHealthState);

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
            SELECT BookId, COUNT(DISTINCT ChapterId), COUNT(*), COALESCE(SUM(FileSize), 0)
            FROM AudioCacheEntries
            WHERE HealthState = $status
            GROUP BY BookId
            ORDER BY COALESCE(SUM(FileSize), 0) DESC, BookId;
            """;
        command.Parameters.AddWithValue("$status", ReadyHealthState);

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
            SELECT e.BookId,
                   c.ChapterIndex,
                   COUNT(DISTINCT e.SegmentKind || ':' || e.SourceStartOffset || ':' || e.SourceLength),
                   COUNT(*),
                   COALESCE(SUM(e.FileSize), 0)
            FROM AudioCacheEntries e
            INNER JOIN Chapters c ON c.Id = e.ChapterId
            WHERE e.HealthState = $status AND e.BookId = $bookId
            GROUP BY e.BookId, c.ChapterIndex
            ORDER BY c.ChapterIndex;
            """;
        command.Parameters.AddWithValue("$status", ReadyHealthState);
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
        CancellationToken cancellationToken) =>
        ReadEntriesAsync(
            bookId,
            chapterIndex,
            "e.LastAccessedAt, e.CreatedAt, e.CacheKey",
            includeStatusFilter: true,
            cancellationToken);

    public Task<IReadOnlyList<AudioCacheIndexEntry>> GetAllEntriesAsync(CancellationToken cancellationToken) =>
        ReadEntriesAsync(
            bookId: null,
            chapterIndex: null,
            "e.CacheKey",
            includeStatusFilter: false,
            cancellationToken);

    public Task<IReadOnlyList<AudioCacheIndexEntry>> GetLruEntriesAsync(CancellationToken cancellationToken) =>
        ReadEntriesAsync(
            bookId: null,
            chapterIndex: null,
            "e.LastAccessedAt, e.CreatedAt, e.CacheKey",
            includeStatusFilter: true,
            cancellationToken);

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
            predicates.Add("e.HealthState = $status");
            command.Parameters.AddWithValue("$status", ReadyHealthState);
        }

        if (bookId is not null)
        {
            predicates.Add("e.BookId = $bookId");
            command.Parameters.AddWithValue("$bookId", bookId);
        }

        if (chapterIndex is not null)
        {
            predicates.Add("c.ChapterIndex = $chapterIndex");
            command.Parameters.AddWithValue("$chapterIndex", chapterIndex.Value);
        }

        command.CommandText =
            $"""
            SELECT e.CacheKey, e.FilePath, e.FileSize
            FROM AudioCacheEntries e
            INNER JOIN Chapters c ON c.Id = e.ChapterId
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
        var cacheKey = reader.GetFieldValue<byte[]>(0);
        return new AudioCacheIndexEntry(
            Encoding.UTF8.GetString(cacheKey),
            reader.GetString(1),
            reader.GetInt64(2));
    }

    private static byte[] ToCacheKeyBlob(string value) => Encoding.UTF8.GetBytes(value);

    private static async Task<string> ResolveChapterIdAsync(
        SqliteConnection connection,
        AudioCacheWriteRequest request,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id FROM Chapters WHERE BookId = $bookId AND ChapterIndex = $chapterIndex LIMIT 1;";
        command.Parameters.AddWithValue("$bookId", request.BookId);
        command.Parameters.AddWithValue("$chapterIndex", request.ChapterIndex);
        var chapterId = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (string.IsNullOrWhiteSpace(chapterId))
        {
            throw new InvalidDataException("音频缓存所属章节不存在。");
        }

        return chapterId;
    }

    private static async Task EnsureSynthesisProfileAsync(
        SqliteConnection connection,
        Fingerprint fingerprint,
        long ruleId,
        string now,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO SynthesisProfiles
                (Fingerprint, SchemaVersion, RuleId, RuleFingerprint, SpeakSpeed, CreatedAt)
            VALUES ($fingerprint, 1, $ruleId, $ruleFingerprint, 0, $createdAt);
            """;
        command.Parameters.AddWithValue("$fingerprint", fingerprint.ToArray());
        command.Parameters.AddWithValue("$ruleId", ruleId);
        command.Parameters.AddWithValue("$ruleFingerprint", fingerprint.ToArray());
        command.Parameters.AddWithValue("$createdAt", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed record AudioCacheIndexEntry(
    string CacheKey,
    string FilePath,
    long FileSize);

internal sealed record AudioCacheIndexSummary(
    long TotalSizeBytes,
    int EntryCount);
