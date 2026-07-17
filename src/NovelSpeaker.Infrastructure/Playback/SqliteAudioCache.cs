using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Persists generated playback audio into the local cache directory and SQLite index.
/// </summary>
public sealed class SqliteAudioCache : IAudioCache, IAudioCacheManagementService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IAudioCacheLimitProvider _cacheLimitProvider;
    private readonly IAudioCacheProtectionRegistry _protectionRegistry;
    private readonly IAppStoragePathResolver _pathResolver;
    private readonly string _versionRootPath;
    private readonly string _ttsRootPath;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public SqliteAudioCache(
        ISqliteConnectionFactory connectionFactory,
        IAppDataDirectoryProvider directories,
        IAudioCacheLimitProvider cacheLimitProvider,
        IAudioCacheProtectionRegistry protectionRegistry,
        IAppStoragePathResolver pathResolver)
    {
        _connectionFactory = connectionFactory;
        _cacheLimitProvider = cacheLimitProvider;
        _protectionRegistry = protectionRegistry;
        _pathResolver = pathResolver;
        _ttsRootPath = Path.Combine(directories.CacheDirectoryPath, "Tts");
        _versionRootPath = Path.Combine(_ttsRootPath, AudioCacheKey.CurrentVersion);
    }

    public Task<AudioCacheEntry?> TryGetAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        return RunExclusiveAsync(ct => TryGetCoreAsync(key, ct), cancellationToken);
    }

    public Task<AudioCacheEntry> StoreAsync(AudioCacheWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunExclusiveAsync(ct => StoreCoreAsync(request, ct), cancellationToken);
    }

    public Task InvalidateAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        return RunExclusiveAsync(ct => InvalidateCoreAsync(key, ct), cancellationToken);
    }

    public Task<AudioCacheSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        return RunExclusiveAsync(GetSummaryCoreAsync, cancellationToken);
    }

    public Task<IReadOnlyList<CachedBookSummary>> GetBooksAsync(CancellationToken cancellationToken)
    {
        return RunExclusiveAsync(GetBooksCoreAsync, cancellationToken);
    }

    public Task<IReadOnlyList<CachedChapterSummary>> GetChaptersAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return RunExclusiveAsync(ct => GetChaptersCoreAsync(bookId, ct), cancellationToken);
    }

    public Task<AudioCacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return RunExclusiveAsync(ct => ClearEntriesCoreAsync(
            "WHERE BookId = $bookId AND ChapterIndex = $chapterIndex",
            command =>
            {
                command.Parameters.AddWithValue("$bookId", bookId);
                command.Parameters.AddWithValue("$chapterIndex", chapterIndex);
            },
            ct), cancellationToken);
    }

    public Task<AudioCacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return RunExclusiveAsync(ct => ClearEntriesCoreAsync(
            "WHERE BookId = $bookId",
            command => command.Parameters.AddWithValue("$bookId", bookId),
            ct), cancellationToken);
    }

    public Task<AudioCacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
    {
        return RunExclusiveAsync(ClearAllCoreAsync, cancellationToken);
    }

    public Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        return RunExclusiveAsync(RunMaintenanceCoreAsync, cancellationToken);
    }

    private async Task<AudioCacheEntry?> TryGetCoreAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var select = connection.CreateCommand();
        select.CommandText =
            """
            SELECT FilePath
            FROM AudioCacheEntries
            WHERE CacheKey = $cacheKey
            LIMIT 1;
            """;
        select.Parameters.AddWithValue("$cacheKey", key.Value);

        var persistedPath = (string?)await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(persistedPath))
        {
            return null;
        }

        var filePath = _pathResolver.ResolvePath(persistedPath);

        if (!File.Exists(filePath))
        {
            await DeleteEntryByKeyAsync(connection, key.Value, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var update = connection.CreateCommand();
        update.CommandText =
            """
            UPDATE AudioCacheEntries
            SET LastAccessedAt = $lastAccessedAt,
                FilePath = $filePath
            WHERE CacheKey = $cacheKey;
            """;
        update.Parameters.AddWithValue("$cacheKey", key.Value);
        update.Parameters.AddWithValue("$lastAccessedAt", DateTime.UtcNow.ToString("O"));
        update.Parameters.AddWithValue("$filePath", _pathResolver.GetStorageKey(filePath));
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return new AudioCacheEntry(key, filePath);
    }

    private async Task<AudioCacheEntry> StoreCoreAsync(AudioCacheWriteRequest request, CancellationToken cancellationToken)
    {
        var extension = NormalizeExtension(Path.GetExtension(request.SourceFilePath));
        var shardDirectory = Path.Combine(_versionRootPath, request.Key.Shard);
        Directory.CreateDirectory(shardDirectory);

        var finalPath = Path.Combine(shardDirectory, $"{request.Key.FileNameBase}{extension}");
        var temporaryPath = Path.Combine(shardDirectory, $"{request.Key.FileNameBase}.{Guid.NewGuid():N}.tmp");

        using var protection = _protectionRegistry.Protect(temporaryPath);
        await CopyOrMoveToTemporaryPathAsync(request.SourceFilePath, temporaryPath, cancellationToken).ConfigureAwait(false);

        try
        {
            if (!File.Exists(finalPath))
            {
                File.Move(temporaryPath, finalPath);
            }
        }
        catch (IOException) when (File.Exists(finalPath))
        {
        }

        TryDeleteFile(temporaryPath);

        var fileInfo = new FileInfo(finalPath);
        if (!fileInfo.Exists)
        {
            throw new IOException($"缓存文件写入失败：{finalPath}");
        }

        using var finalProtection = _protectionRegistry.Protect(finalPath);
        var now = DateTime.UtcNow.ToString("O");
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
        command.Parameters.AddWithValue("$filePath", _pathResolver.GetStorageKey(finalPath));
        command.Parameters.AddWithValue("$contentType", (object?)request.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileSize", fileInfo.Length);
        command.Parameters.AddWithValue("$durationMilliseconds", (object?)request.DurationMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$lastAccessedAt", now);
        command.Parameters.AddWithValue("$status", (int)AudioCacheEntryStatus.Ready);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await EnforceLimitCoreAsync(cancellationToken).ConfigureAwait(false);
        return new AudioCacheEntry(request.Key, finalPath);
    }

    private async Task InvalidateCoreAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var select = connection.CreateCommand();
        select.CommandText =
            """
            SELECT FilePath
            FROM AudioCacheEntries
            WHERE CacheKey = $cacheKey
            LIMIT 1;
            """;
        select.Parameters.AddWithValue("$cacheKey", key.Value);
        var persistedPath = (string?)await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        await DeleteEntryByKeyAsync(connection, key.Value, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(persistedPath))
        {
            TryDeleteFile(_pathResolver.ResolvePath(persistedPath));
        }
    }

    private async Task<AudioCacheSummary> GetSummaryCoreAsync(CancellationToken cancellationToken)
    {
        var cacheLimitBytes = _cacheLimitProvider.GetCurrentLimitBytes();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(SUM(FileSize), 0), COUNT(*)
            FROM AudioCacheEntries
            WHERE Status = $status;
            """;
        command.Parameters.AddWithValue("$status", (int)AudioCacheEntryStatus.Ready);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        var totalSize = reader.GetInt64(0);
        var entryCount = reader.GetInt32(1);
        return new AudioCacheSummary(totalSize, entryCount, cacheLimitBytes, totalSize > cacheLimitBytes);
    }

    private async Task<IReadOnlyList<CachedBookSummary>> GetBooksCoreAsync(CancellationToken cancellationToken)
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
        command.Parameters.AddWithValue("$status", (int)AudioCacheEntryStatus.Ready);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<CachedBookSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new CachedBookSummary(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt64(3)));
        }

        return items;
    }

    private async Task<IReadOnlyList<CachedChapterSummary>> GetChaptersCoreAsync(string bookId, CancellationToken cancellationToken)
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
        command.Parameters.AddWithValue("$status", (int)AudioCacheEntryStatus.Ready);
        command.Parameters.AddWithValue("$bookId", bookId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<CachedChapterSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new CachedChapterSummary(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt64(4)));
        }

        return items;
    }

    private async Task<AudioCacheCleanupResult> ClearEntriesCoreAsync(
        string whereClause,
        Action<SqliteCommand> configureParameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var select = connection.CreateCommand();
        select.CommandText =
            $"""
            SELECT CacheKey, FilePath, FileSize
            FROM AudioCacheEntries
            {whereClause}
            ORDER BY LastAccessedAt;
            """;
        configureParameters(select);

        var items = await ReadEntriesAsync(select, cancellationToken).ConfigureAwait(false);
        var deletedBytes = 0L;
        var deletedEntryCount = 0;
        var protectedEntryCount = 0;
        var failedEntryCount = 0;
        foreach (var item in items)
        {
            if (_protectionRegistry.IsProtected(item.FilePath))
            {
                protectedEntryCount++;
                continue;
            }

            if (!File.Exists(item.FilePath) || TryDeleteFile(item.FilePath))
            {
                await DeleteEntryByKeyAsync(connection, item.CacheKey, cancellationToken).ConfigureAwait(false);
                deletedBytes += item.FileSize;
                deletedEntryCount++;
            }
            else
            {
                failedEntryCount++;
            }
        }

        return new AudioCacheCleanupResult(deletedBytes, deletedEntryCount, protectedEntryCount, failedEntryCount);
    }

    private async Task<AudioCacheCleanupResult> ClearAllCoreAsync(CancellationToken cancellationToken)
    {
        var result = await ClearEntriesCoreAsync(string.Empty, static _ => { }, cancellationToken).ConfigureAwait(false);
        DeleteResidualTemporaryFiles(cancellationToken);
        DeleteOrphanCacheFiles([], cancellationToken);
        return result;
    }

    private async Task RunMaintenanceCoreAsync(CancellationToken cancellationToken)
    {
        DeleteResidualTemporaryFiles(cancellationToken);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var select = connection.CreateCommand();
        select.CommandText =
            """
            SELECT CacheKey, FilePath, FileSize
            FROM AudioCacheEntries;
            """;

        var entries = await ReadEntriesAsync(select, cancellationToken).ConfigureAwait(false);
        var knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            knownPaths.Add(entry.FilePath);
            if (!File.Exists(entry.FilePath))
            {
                await DeleteEntryByKeyAsync(connection, entry.CacheKey, cancellationToken).ConfigureAwait(false);
            }
        }

        DeleteOrphanCacheFiles(knownPaths, cancellationToken);
        await EnforceLimitCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnforceLimitCoreAsync(CancellationToken cancellationToken)
    {
        var cacheLimitBytes = _cacheLimitProvider.GetCurrentLimitBytes();
        var summary = await GetSummaryCoreAsync(cancellationToken).ConfigureAwait(false);
        if (summary.TotalSizeBytes <= cacheLimitBytes)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT CacheKey, FilePath, FileSize
            FROM AudioCacheEntries
            WHERE Status = $status
            ORDER BY LastAccessedAt, CreatedAt, CacheKey;
            """;
        command.Parameters.AddWithValue("$status", (int)AudioCacheEntryStatus.Ready);

        var rows = new List<CacheSizedPath>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new CacheSizedPath(
                    reader.GetString(0),
                    _pathResolver.ResolvePath(reader.GetString(1)),
                    reader.GetInt64(2)));
            }
        }

        var totalSize = summary.TotalSizeBytes;
        foreach (var row in rows)
        {
            if (totalSize <= cacheLimitBytes)
            {
                break;
            }

            if (_protectionRegistry.IsProtected(row.FilePath))
            {
                continue;
            }

            if (!File.Exists(row.FilePath) || TryDeleteFile(row.FilePath))
            {
                await DeleteEntryByKeyAsync(connection, row.CacheKey, cancellationToken).ConfigureAwait(false);
                totalSize = Math.Max(0, totalSize - row.FileSize);
            }
        }
    }

    private void DeleteResidualTemporaryFiles(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_ttsRootPath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(_ttsRootPath, "*.tmp", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_protectionRegistry.IsProtected(filePath))
            {
                continue;
            }

            TryDeleteFile(filePath);
        }
    }

    private void DeleteOrphanCacheFiles(HashSet<string> knownPaths, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_versionRootPath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(_versionRootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetExtension(filePath), ".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedPath = Path.GetFullPath(filePath);
            if (knownPaths.Contains(normalizedPath) || _protectionRegistry.IsProtected(normalizedPath))
            {
                continue;
            }

            TryDeleteFile(normalizedPath);
        }
    }

    private static async Task CopyOrMoveToTemporaryPathAsync(string sourceFilePath, string temporaryPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("未找到要缓存的源音频文件。", sourceFilePath);
        }

        try
        {
            File.Move(sourceFilePath, temporaryPath, overwrite: true);
            return;
        }
        catch (IOException)
        {
        }

        await using var source = File.OpenRead(sourceFilePath);
        await using var destination = File.Create(temporaryPath);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        destination.Close();
        File.Delete(sourceFilePath);
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new InvalidOperationException("缓存音频文件缺少可识别的扩展名。");
        }

        return extension.StartsWith(".", StringComparison.Ordinal)
            ? extension.ToLowerInvariant()
            : $".{extension.ToLowerInvariant()}";
    }

    private static bool TryDeleteFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return true;
            }

            File.Delete(filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<List<CachePath>> ReadEntriesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var items = new List<CachePath>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new CachePath(
                reader.GetString(0),
                _pathResolver.ResolvePath(reader.GetString(1)),
                reader.GetInt64(2)));
        }

        return items;
    }

    private static async Task DeleteEntryByKeyAsync(SqliteConnection connection, string cacheKey, CancellationToken cancellationToken)
    {
        var delete = connection.CreateCommand();
        delete.CommandText =
            """
            DELETE FROM AudioCacheEntries
            WHERE CacheKey = $cacheKey;
            """;
        delete.Parameters.AddWithValue("$cacheKey", cacheKey);
        await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task RunExclusiveAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private enum AudioCacheEntryStatus
    {
        Ready = 1
    }

    private sealed record CachePath(string CacheKey, string FilePath, long FileSize);

    private sealed record CacheSizedPath(string CacheKey, string FilePath, long FileSize);
}
