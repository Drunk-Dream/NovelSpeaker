using System.Text;
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Domain.Books;
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
            WHERE CacheKey = $cacheKey AND KeyVersion = $keyVersion AND HealthState = $status
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$cacheKey", ToCacheKeyBlob(key.Value));
        command.Parameters.AddWithValue("$keyVersion", 2);
        command.Parameters.AddWithValue("$status", ReadyHealthState);

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
                WHERE KeyVersion = 2 AND HealthState = {ReadyHealthState}
                  AND CacheKey IN ({string.Join(", ", parameterNames)});
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
            WHERE CacheKey = $cacheKey AND KeyVersion = $keyVersion;
            """;
        command.Parameters.AddWithValue("$cacheKey", ToCacheKeyBlob(cacheKey));
        command.Parameters.AddWithValue("$keyVersion", 2);
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
        await EnsureSynthesisProfileAsync(
            connection,
            request.Key.Identity.SynthesisProfile,
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
        command.Parameters.AddWithValue("$keyVersion", 2);
        command.Parameters.AddWithValue("$bookId", request.BookId);
        command.Parameters.AddWithValue("$chapterId", chapterId);
        command.Parameters.AddWithValue("$segmentKind", (int)request.Key.Identity.Segment.Kind);
        command.Parameters.AddWithValue("$sourceStartOffset", request.Key.Identity.Segment.SourceStartOffset);
        command.Parameters.AddWithValue("$sourceLength", Math.Max(1, request.Key.Identity.Segment.SourceLength));
        command.Parameters.AddWithValue("$speechTextHash", request.Key.Identity.SpeechTextHash.ToArray());
        command.Parameters.AddWithValue(
            "$synthesisProfileFingerprint",
            request.Key.Identity.SynthesisProfile.Value.ToArray());
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
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var chapterCommand = connection.CreateCommand();
            chapterCommand.Transaction = transaction;
            chapterCommand.CommandText =
                """
                SELECT ChapterId
                FROM AudioCacheEntries
                WHERE CacheKey = $cacheKey AND KeyVersion = 2
                LIMIT 1;
                """;
            chapterCommand.Parameters.AddWithValue("$cacheKey", ToCacheKeyBlob(cacheKey));
            var chapterId = Convert.ToString(
                await chapterCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

            var deleteEntry = connection.CreateCommand();
            deleteEntry.Transaction = transaction;
            deleteEntry.CommandText =
                """
                DELETE FROM AudioCacheEntries
                WHERE CacheKey = $cacheKey AND KeyVersion = 2;
                """;
            deleteEntry.Parameters.AddWithValue("$cacheKey", ToCacheKeyBlob(cacheKey));
            var deletedEntryCount = await deleteEntry
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deletedEntryCount > 0 && !string.IsNullOrWhiteSpace(chapterId))
            {
                var deletePlan = connection.CreateCommand();
                deletePlan.Transaction = transaction;
                deletePlan.CommandText =
                    """
                    DELETE FROM ChapterSpeechPlans
                    WHERE ChapterId = $chapterId
                      AND NOT EXISTS (
                          SELECT 1
                          FROM AudioCacheEntries
                          WHERE ChapterId = $chapterId
                          LIMIT 1);
                    """;
                deletePlan.Parameters.AddWithValue("$chapterId", chapterId);
                await deletePlan.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<AudioCacheIndexSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(SUM(FileSize), 0), COUNT(*)
            FROM AudioCacheEntries
            WHERE KeyVersion = 2 AND HealthState = $status;
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
            WHERE KeyVersion = 2 AND HealthState = $status
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
            WHERE e.KeyVersion = 2 AND e.HealthState = $status AND e.BookId = $bookId
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

    public async Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(
        IReadOnlyCollection<CurrentCacheChapterQuery> chapters,
        SynthesisProfileFingerprint synthesisProfile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentNullException.ThrowIfNull(synthesisProfile);

        var requested = chapters
            .Where(chapter => !string.IsNullOrWhiteSpace(chapter.ChapterId))
            .GroupBy(chapter => chapter.ChapterId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (requested.Length == 0)
        {
            return [];
        }

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        var statusesByChapterId = new Dictionary<string, ChapterCacheStatus>(
            requested.Length,
            StringComparer.Ordinal);

        // Five request parameters per chapter keep each command below SQLite's default
        // parameter limit while retaining one connection for the whole refresh.
        const int batchSize = 160;
        for (var offset = 0; offset < requested.Length; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchCount = Math.Min(batchSize, requested.Length - offset);
            var values = new string[batchCount];
            using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("$synthesisProfile", synthesisProfile.Value.ToArray());

            for (var index = 0; index < batchCount; index++)
            {
                var chapter = requested[offset + index];
                var chapterIdParameter = $"$chapterId{index}";
                var chapterIndexParameter = $"$chapterIndex{index}";
                var readTitleParameter = $"$readTitle{index}";
                var titleHashParameter = $"$titleHash{index}";
                var textProfileParameter = $"$textProfile{index}";
                values[index] =
                    $"({chapterIdParameter}, {chapterIndexParameter}, {readTitleParameter}, {titleHashParameter}, {textProfileParameter})";
                command.Parameters.AddWithValue(chapterIdParameter, chapter.ChapterId);
                command.Parameters.AddWithValue(chapterIndexParameter, chapter.ChapterIndex);
                command.Parameters.AddWithValue(readTitleParameter, chapter.ReadChapterTitle ? 1 : 0);
                command.Parameters.AddWithValue(
                    titleHashParameter,
                    (object?)chapter.ChapterTitleSpeechTextHash?.ToArray() ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    textProfileParameter,
                    (object?)chapter.TextProfileFingerprint?.Value.ToArray() ?? DBNull.Value);
            }

            command.CommandText =
                $"""
                WITH requested(
                    ChapterId,
                    ChapterIndex,
                    ReadChapterTitle,
                    ChapterTitleSpeechTextHash,
                    TextProfileFingerprint) AS (
                    VALUES {string.Join(", ", values)}
                )
                SELECT r.ChapterId,
                       r.ChapterIndex,
                       p.State,
                       CASE
                           WHEN p.ChapterId IS NOT NULL
                                AND r.TextProfileFingerprint IS NOT NULL
                                AND p.TextProfileFingerprint <> r.TextProfileFingerprint
                           THEN 1
                           ELSE 0
                       END,
                       p.BodySegmentCount,
                       COALESCE(SUM(CASE WHEN e.CacheKey IS NOT NULL THEN 1 ELSE 0 END), 0),
                       CASE
                           WHEN r.ReadChapterTitle = 1 AND r.ChapterTitleSpeechTextHash IS NOT NULL
                           THEN 1
                           ELSE 0
                       END,
                       CASE
                           WHEN r.ReadChapterTitle = 1
                                AND r.ChapterTitleSpeechTextHash IS NOT NULL
                                AND EXISTS (
                                    SELECT 1
                                    FROM AudioCacheEntries titleEntry
                                    WHERE titleEntry.ChapterId = r.ChapterId
                                      AND titleEntry.KeyVersion = 2
                                      AND titleEntry.HealthState = {ReadyHealthState}
                                      AND titleEntry.SynthesisProfileFingerprint = $synthesisProfile
                                      AND titleEntry.SegmentKind = {(int)SpeechSegmentKind.ChapterTitle}
                                      AND titleEntry.SourceStartOffset = 0
                                      AND titleEntry.SourceLength = 1
                                      AND titleEntry.SpeechTextHash = r.ChapterTitleSpeechTextHash
                                    LIMIT 1)
                           THEN 1
                           ELSE 0
                       END
                FROM requested r
                LEFT JOIN ChapterSpeechPlans p
                       ON p.ChapterId = r.ChapterId
                LEFT JOIN ChapterSpeechPlanSegments s
                       ON s.ChapterId = p.ChapterId
                      AND p.State = {(int)ChapterSpeechPlanState.Ready}
                      AND s.SegmentKind = {(int)SpeechSegmentKind.Body}
                LEFT JOIN AudioCacheEntries e
                       ON e.ChapterId = s.ChapterId
                      AND e.KeyVersion = 2
                      AND e.HealthState = {ReadyHealthState}
                      AND e.SynthesisProfileFingerprint = $synthesisProfile
                      AND e.SegmentKind = s.SegmentKind
                      AND e.SourceStartOffset = s.SourceStartOffset
                      AND e.SourceLength = s.SourceLength
                      AND e.SpeechTextHash = s.SpeechTextHash
                GROUP BY r.ChapterId,
                         r.ChapterIndex,
                         r.ReadChapterTitle,
                         r.ChapterTitleSpeechTextHash,
                         r.TextProfileFingerprint,
                         p.TextProfileFingerprint,
                         p.State,
                         p.BodySegmentCount;
                """;

            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var chapterId = reader.GetString(0);
                var chapterIndex = reader.GetInt32(1);
                int? planState = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                var planIsStale = reader.GetInt32(3) != 0;
                var bodySegmentCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                var cachedBodySegmentCount = reader.GetInt32(5);
                var titleSegmentCount = reader.GetInt32(6);
                var cachedTitleSegmentCount = reader.GetInt32(7);
                statusesByChapterId[chapterId] = CreateCurrentConfigurationStatus(
                    chapterIndex,
                    planState,
                    planIsStale,
                    bodySegmentCount,
                    cachedBodySegmentCount + cachedTitleSegmentCount,
                    titleSegmentCount);
            }
        }

        return requested
            .Select(chapter => statusesByChapterId.GetValueOrDefault(
                chapter.ChapterId,
                new ChapterCacheStatus(chapter.ChapterIndex, 0, null)
                {
                    Kind = ChapterCacheStatusKind.PlanMissing
                }))
            .ToArray();
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

    public async Task<IReadOnlyList<AudioCacheMaintenanceEntry>> GetMaintenanceEntriesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT e.CacheKey,
                   e.FilePath,
                   e.FileSize,
                   e.BookId,
                   c.ChapterIndex,
                   e.LastAccessedAt,
                   e.ValidatedAt
            FROM AudioCacheEntries e
            LEFT JOIN Chapters c ON c.Id = e.ChapterId
            WHERE e.KeyVersion = 2
            ORDER BY e.LastAccessedAt, e.CacheKey;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<AudioCacheMaintenanceEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new AudioCacheMaintenanceEntry(
                Encoding.UTF8.GetString(reader.GetFieldValue<byte[]>(0)),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                SqliteDateTimeMapper.Parse(reader.GetString(5)),
                SqliteDateTimeMapper.TryParse(reader.GetString(6), out var validatedAt)
                    ? validatedAt
                    : null));
        }

        return entries;
    }

    public async Task MarkValidatedAsync(
        string cacheKey,
        DateTimeOffset validatedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE AudioCacheEntries
            SET HealthState = $healthState,
                ValidatedAt = $validatedAt
            WHERE CacheKey = $cacheKey AND KeyVersion = 2;
            """;
        command.Parameters.AddWithValue("$healthState", ReadyHealthState);
        command.Parameters.AddWithValue("$validatedAt", SqliteDateTimeMapper.Format(validatedAt));
        command.Parameters.AddWithValue("$cacheKey", ToCacheKeyBlob(cacheKey));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

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
        predicates.Add("e.KeyVersion = 2");
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

    private static ChapterCacheStatus CreateCurrentConfigurationStatus(
        int chapterIndex,
        int? planState,
        bool planIsStale,
        int bodySegmentCount,
        int cachedSegmentCount,
        int titleSegmentCount)
    {
        if (planState is null)
        {
            return new ChapterCacheStatus(chapterIndex, 0, null)
            {
                Kind = ChapterCacheStatusKind.PlanMissing
            };
        }

        if (planIsStale)
        {
            return new ChapterCacheStatus(chapterIndex, 0, null)
            {
                Kind = ChapterCacheStatusKind.PlanStale
            };
        }

        if (planState.Value != (int)ChapterSpeechPlanState.Ready)
        {
            return new ChapterCacheStatus(chapterIndex, 0, null)
            {
                Kind = ChapterCacheStatusKind.PlanUnavailable
            };
        }

        var totalSegmentCount = Math.Max(0, bodySegmentCount) + titleSegmentCount;
        if (totalSegmentCount == 0)
        {
            return new ChapterCacheStatus(chapterIndex, 0, 0)
            {
                Kind = ChapterCacheStatusKind.NoPlayableContent
            };
        }

        return new ChapterCacheStatus(
            chapterIndex,
            Math.Clamp(cachedSegmentCount, 0, totalSegmentCount),
            totalSegmentCount);
    }

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
        SynthesisProfileFingerprint profile,
        long ruleId,
        string now,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO SynthesisProfiles
                (Fingerprint, SchemaVersion, RuleId, RuleFingerprint, SpeakSpeed, OptionsJson, CreatedAt)
            VALUES ($fingerprint, $schemaVersion, $ruleId, $ruleFingerprint, $speakSpeed, $optionsJson, $createdAt);
            """;
        command.Parameters.AddWithValue("$fingerprint", profile.Value.ToArray());
        command.Parameters.AddWithValue("$schemaVersion", profile.SchemaVersion);
        command.Parameters.AddWithValue("$ruleId", ruleId);
        command.Parameters.AddWithValue("$ruleFingerprint", profile.TtsRule.Value.ToArray());
        command.Parameters.AddWithValue("$speakSpeed", profile.SpeakSpeed);
        command.Parameters.AddWithValue("$optionsJson", (object?)profile.OptionsJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed record AudioCacheIndexEntry(
    string CacheKey,
    string FilePath,
    long FileSize);

internal sealed record AudioCacheMaintenanceEntry(
    string CacheKey,
    string FilePath,
    long FileSize,
    string BookId,
    int? ChapterIndex,
    DateTimeOffset LastAccessedAt,
    DateTimeOffset? ValidatedAt);

internal sealed record AudioCacheIndexSummary(
    long TotalSizeBytes,
    int EntryCount);
