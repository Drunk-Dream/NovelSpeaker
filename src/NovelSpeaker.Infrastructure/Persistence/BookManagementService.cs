using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Provides book details, metadata updates, cache cleanup, and atomic deletion.
/// </summary>
public sealed class BookManagementService : IBookManagementService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IAppDataDirectoryProvider _directories;
    private readonly IAudioCacheManagementService _audioCacheManagementService;
    private readonly IAudioCacheProtectionRegistry _audioCacheProtectionRegistry;

    public BookManagementService(
        ISqliteConnectionFactory connectionFactory,
        IAppDataDirectoryProvider directories,
        IAudioCacheManagementService audioCacheManagementService,
        IAudioCacheProtectionRegistry audioCacheProtectionRegistry)
    {
        _connectionFactory = connectionFactory;
        _directories = directories;
        _audioCacheManagementService = audioCacheManagementService;
        _audioCacheProtectionRegistry = audioCacheProtectionRegistry;
    }

    public async Task<BookDetails?> GetBookDetailsAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await GetBookDetailsCoreAsync(connection, bookId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BookDetails> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trimmedTitle = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            throw new InvalidOperationException("书名不能为空。");
        }

        var normalizedAuthor = string.IsNullOrWhiteSpace(request.Author)
            ? null
            : request.Author.Trim();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var updateCommand = connection.CreateCommand();
        updateCommand.CommandText =
            """
            UPDATE Books
            SET Title = $title,
                Author = $author,
                UpdatedAt = $updatedAt
            WHERE Id = $bookId;
            """;
        updateCommand.Parameters.AddWithValue("$bookId", request.BookId);
        updateCommand.Parameters.AddWithValue("$title", trimmedTitle);
        updateCommand.Parameters.AddWithValue("$author", (object?)normalizedAuthor ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));

        var affectedRows = await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affectedRows == 0)
        {
            throw new InvalidOperationException("未找到要更新的书籍。");
        }

        return (await GetBookDetailsCoreAsync(connection, request.BookId, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<long> ClearBookCacheAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        var result = await _audioCacheManagementService.ClearBookAsync(bookId, cancellationToken).ConfigureAwait(false);
        return result.DeletedBytes;
    }

    public async Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var existingDetails = await GetBookDetailsCoreAsync(connection, request.BookId, cancellationToken).ConfigureAwait(false);
        if (existingDetails is null)
        {
            return null;
        }

        var stageRoot = Path.Combine(_directories.RootDirectoryPath, ".deletions", Guid.NewGuid().ToString("N"));
        var stagedPaths = new List<StagedPath>();

        try
        {
            Directory.CreateDirectory(stageRoot);
            await StageBookFilesAsync(existingDetails.StoredFilePath, stageRoot, stagedPaths, cancellationToken).ConfigureAwait(false);

            if (request.DeleteAudioCache)
            {
                await StageCachedAudioFilesAsync(connection, request.BookId, stageRoot, stagedPaths, cancellationToken).ConfigureAwait(false);
            }

            var deletedChapterCount = existingDetails.TotalChapterCount;
            var deletedReadingProgress = existingDetails.HasReadingProgress;

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (request.DeleteAudioCache)
                {
                    var deleteCacheCommand = connection.CreateCommand();
                    deleteCacheCommand.Transaction = transaction;
                    deleteCacheCommand.CommandText = "DELETE FROM AudioCacheEntries WHERE BookId = $bookId;";
                    deleteCacheCommand.Parameters.AddWithValue("$bookId", request.BookId);
                    await deleteCacheCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                var deleteProgressCommand = connection.CreateCommand();
                deleteProgressCommand.Transaction = transaction;
                deleteProgressCommand.CommandText = "DELETE FROM ReadingProgress WHERE BookId = $bookId;";
                deleteProgressCommand.Parameters.AddWithValue("$bookId", request.BookId);
                await deleteProgressCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                var deleteChaptersCommand = connection.CreateCommand();
                deleteChaptersCommand.Transaction = transaction;
                deleteChaptersCommand.CommandText = "DELETE FROM Chapters WHERE BookId = $bookId;";
                deleteChaptersCommand.Parameters.AddWithValue("$bookId", request.BookId);
                await deleteChaptersCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                var deleteBookCommand = connection.CreateCommand();
                deleteBookCommand.Transaction = transaction;
                deleteBookCommand.CommandText = "DELETE FROM Books WHERE Id = $bookId;";
                deleteBookCommand.Parameters.AddWithValue("$bookId", request.BookId);
                await deleteBookCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }

            DeleteStagedPaths(stagedPaths);
            TryDeleteDirectory(stageRoot);

            return new BookDeleteResult(
                request.BookId,
                request.DeleteAudioCache,
                deletedChapterCount,
                deletedReadingProgress);
        }
        catch
        {
            RestoreStagedPaths(stagedPaths);
            TryDeleteDirectory(stageRoot);
            throw;
        }
    }

    private async Task<BookDetails?> GetBookDetailsCoreAsync(
        SqliteConnection connection,
        string bookId,
        CancellationToken cancellationToken)
    {
        var bookCommand = connection.CreateCommand();
        bookCommand.CommandText =
            """
            SELECT b.Id,
                   b.Title,
                   b.Author,
                   b.OriginalFileName,
                   b.StoredFilePath,
                   b.Encoding,
                   COALESCE(chapterCounts.TotalChapterCount, 0) AS TotalChapterCount,
                   rp.ChapterIndex,
                   CASE WHEN rp.BookId IS NULL THEN 0 ELSE 1 END AS HasReadingProgress,
                   COALESCE(cache.TotalSizeBytes, 0) AS CachedAudioBytes
            FROM Books b
            LEFT JOIN (
                SELECT BookId, COUNT(*) AS TotalChapterCount
                FROM Chapters
                GROUP BY BookId
            ) chapterCounts
                ON chapterCounts.BookId = b.Id
            LEFT JOIN ReadingProgress rp
                ON rp.BookId = b.Id
            LEFT JOIN (
                SELECT BookId, COALESCE(SUM(FileSize), 0) AS TotalSizeBytes
                FROM AudioCacheEntries
                GROUP BY BookId
            ) cache
                ON cache.BookId = b.Id
            WHERE b.Id = $bookId
            LIMIT 1;
            """;
        bookCommand.Parameters.AddWithValue("$bookId", bookId);

        string id;
        string title;
        string? author;
        string originalFileName;
        string storedFilePath;
        string encoding;
        int totalChapterCount;
        int? currentChapterIndex;
        bool hasReadingProgress;
        long cachedAudioBytes;

        await using (var bookReader = await bookCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await bookReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            id = bookReader.GetString(0);
            title = bookReader.GetString(1);
            author = bookReader.IsDBNull(2) ? null : bookReader.GetString(2);
            originalFileName = bookReader.GetString(3);
            storedFilePath = bookReader.GetString(4);
            encoding = bookReader.GetString(5);
            totalChapterCount = bookReader.GetInt32(6);
            currentChapterIndex = bookReader.IsDBNull(7) ? null : bookReader.GetInt32(7);
            hasReadingProgress = bookReader.GetInt64(8) == 1 && currentChapterIndex is not null;
            cachedAudioBytes = bookReader.GetInt64(9);
        }

        var clampedCurrentChapterIndex = hasReadingProgress && totalChapterCount > 0
            ? Math.Clamp(currentChapterIndex!.Value, 0, totalChapterCount - 1)
            : (int?)null;

        var chaptersCommand = connection.CreateCommand();
        chaptersCommand.CommandText =
            """
            SELECT ChapterIndex, Title, StartOffset, Length
            FROM Chapters
            WHERE BookId = $bookId
            ORDER BY SortOrder, ChapterIndex;
            """;
        chaptersCommand.Parameters.AddWithValue("$bookId", bookId);

        var chapters = new List<BookChapterSummary>();
        await using var chapterReader = await chaptersCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await chapterReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var chapterIndex = chapterReader.GetInt32(0);
            chapters.Add(new BookChapterSummary(
                chapterIndex,
                chapterReader.GetString(1),
                chapterReader.GetInt32(2),
                chapterReader.GetInt32(3),
                clampedCurrentChapterIndex == chapterIndex));
        }

        return new BookDetails(
            id,
            title,
            author,
            originalFileName,
            storedFilePath,
            encoding,
            totalChapterCount,
            clampedCurrentChapterIndex,
            hasReadingProgress && clampedCurrentChapterIndex is not null
                ? Math.Max(0, totalChapterCount - (clampedCurrentChapterIndex.Value + 1))
                : totalChapterCount,
            hasReadingProgress && clampedCurrentChapterIndex is not null && totalChapterCount > 0
                ? (double)(clampedCurrentChapterIndex.Value + 1) / totalChapterCount
                : 0,
            hasReadingProgress,
            cachedAudioBytes,
            chapters);
    }

    private async Task<long> GetBookCachedBytesAsync(string bookId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(SUM(FileSize), 0)
            FROM AudioCacheEntries
            WHERE BookId = $bookId;
            """;
        command.Parameters.AddWithValue("$bookId", bookId);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task StageBookFilesAsync(
        string storedFilePath,
        string stageRoot,
        List<StagedPath> stagedPaths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bookDirectory = Path.GetDirectoryName(storedFilePath);
        if (string.IsNullOrWhiteSpace(bookDirectory) || !Directory.Exists(bookDirectory))
        {
            return;
        }

        var stagedDirectory = Path.Combine(stageRoot, "book");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedDirectory)!);
        Directory.Move(bookDirectory, stagedDirectory);
        stagedPaths.Add(new StagedPath(bookDirectory, stagedDirectory, true));
    }

    private async Task StageCachedAudioFilesAsync(
        SqliteConnection connection,
        string bookId,
        string stageRoot,
        List<StagedPath> stagedPaths,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT FilePath
            FROM AudioCacheEntries
            WHERE BookId = $bookId
            ORDER BY CacheKey;
            """;
        command.Parameters.AddWithValue("$bookId", bookId);

        var cacheDirectory = Path.Combine(stageRoot, "cache");
        Directory.CreateDirectory(cacheDirectory);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var filePath = reader.GetString(0);
            if (_audioCacheProtectionRegistry.IsProtected(filePath))
            {
                throw new InvalidOperationException("无法删除当前仍受保护的缓存文件。");
            }

            if (!File.Exists(filePath))
            {
                continue;
            }

            var stagedFilePath = Path.Combine(cacheDirectory, $"{Guid.NewGuid():N}{Path.GetExtension(filePath)}");
            Directory.CreateDirectory(Path.GetDirectoryName(stagedFilePath)!);
            File.Move(filePath, stagedFilePath, overwrite: true);
            stagedPaths.Add(new StagedPath(filePath, stagedFilePath, false));
        }
    }

    private static void RestoreStagedPaths(IReadOnlyList<StagedPath> stagedPaths)
    {
        foreach (var stagedPath in stagedPaths.Reverse())
        {
            if (stagedPath.IsDirectory)
            {
                if (Directory.Exists(stagedPath.TemporaryPath) && !Directory.Exists(stagedPath.OriginalPath))
                {
                    Directory.Move(stagedPath.TemporaryPath, stagedPath.OriginalPath);
                }

                continue;
            }

            if (File.Exists(stagedPath.TemporaryPath) && !File.Exists(stagedPath.OriginalPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath.OriginalPath)!);
                File.Move(stagedPath.TemporaryPath, stagedPath.OriginalPath, overwrite: true);
            }
        }
    }

    private static void DeleteStagedPaths(IReadOnlyList<StagedPath> stagedPaths)
    {
        foreach (var stagedPath in stagedPaths.OrderByDescending(path => path.IsDirectory))
        {
            if (stagedPath.IsDirectory)
            {
                TryDeleteDirectory(stagedPath.TemporaryPath);
                continue;
            }

            TryDeleteFile(stagedPath.TemporaryPath);
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private sealed record StagedPath(
        string OriginalPath,
        string TemporaryPath,
        bool IsDirectory);
}
