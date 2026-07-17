using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>
/// Deletes book-owned database rows and staged files with rollback compensation.
/// </summary>
public sealed class BookDeletionService : IBookDeletionService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IAppDataDirectoryProvider _directories;
    private readonly IAudioCacheProtectionRegistry _audioCacheProtectionRegistry;

    public BookDeletionService(
        ISqliteConnectionFactory connectionFactory,
        IAppDataDirectoryProvider directories,
        IAudioCacheProtectionRegistry audioCacheProtectionRegistry)
    {
        _connectionFactory = connectionFactory;
        _directories = directories;
        _audioCacheProtectionRegistry = audioCacheProtectionRegistry;
    }

    public async Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var book = await ReadDeletionTargetAsync(connection, request.BookId, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return null;
        }

        var stageRoot = Path.Combine(_directories.RootDirectoryPath, ".deletions", Guid.NewGuid().ToString("N"));
        var stagedPaths = new List<StagedPath>();
        try
        {
            Directory.CreateDirectory(stageRoot);
            StageBookFiles(book.StoredFilePath, stageRoot, stagedPaths, cancellationToken);
            if (request.DeleteAudioCache)
            {
                await StageCachedAudioFilesAsync(connection, request.BookId, stageRoot, stagedPaths, cancellationToken).ConfigureAwait(false);
            }

            await DeleteRowsAsync(connection, request, cancellationToken).ConfigureAwait(false);
            DeleteStagedPaths(stagedPaths);
            TryDeleteDirectory(stageRoot);
            return new BookDeleteResult(request.BookId, request.DeleteAudioCache, book.ChapterCount, book.HasReadingProgress);
        }
        catch
        {
            RestoreStagedPaths(stagedPaths);
            TryDeleteDirectory(stageRoot);
            throw;
        }
    }

    private static async Task<DeletionTarget?> ReadDeletionTargetAsync(
        SqliteConnection connection,
        string bookId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT b.StoredFilePath,
                   (SELECT COUNT(*) FROM Chapters c WHERE c.BookId = b.Id),
                   CASE WHEN EXISTS (SELECT 1 FROM ReadingProgress rp WHERE rp.BookId = b.Id) THEN 1 ELSE 0 END
            FROM Books b
            WHERE b.Id = $bookId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$bookId", bookId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new DeletionTarget(reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2) == 1)
            : null;
    }

    private static async Task DeleteRowsAsync(
        SqliteConnection connection,
        BookDeleteRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (request.DeleteAudioCache)
            {
                await ExecuteDeleteAsync(
                    connection,
                    transaction,
                    "DELETE FROM AudioCacheEntries WHERE BookId = $bookId;",
                    request.BookId,
                    cancellationToken).ConfigureAwait(false);
            }

            await ExecuteDeleteAsync(
                connection,
                transaction,
                "DELETE FROM ReadingProgress WHERE BookId = $bookId;",
                request.BookId,
                cancellationToken).ConfigureAwait(false);
            await ExecuteDeleteAsync(
                connection,
                transaction,
                "DELETE FROM Chapters WHERE BookId = $bookId;",
                request.BookId,
                cancellationToken).ConfigureAwait(false);

            var bookCommand = connection.CreateCommand();
            bookCommand.Transaction = transaction;
            bookCommand.CommandText = "DELETE FROM Books WHERE Id = $bookId;";
            bookCommand.Parameters.AddWithValue("$bookId", request.BookId);
            await bookCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ExecuteDeleteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        string bookId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$bookId", bookId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void StageBookFiles(string storedFilePath, string stageRoot, List<StagedPath> stagedPaths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bookDirectory = Path.GetDirectoryName(storedFilePath);
        if (string.IsNullOrWhiteSpace(bookDirectory) || !Directory.Exists(bookDirectory))
        {
            return;
        }

        var stagedDirectory = Path.Combine(stageRoot, "book");
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
        command.CommandText = "SELECT FilePath FROM AudioCacheEntries WHERE BookId = $bookId ORDER BY CacheKey;";
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
            File.Move(filePath, stagedFilePath, overwrite: true);
            stagedPaths.Add(new StagedPath(filePath, stagedFilePath, false));
        }
    }

    private static void RestoreStagedPaths(IReadOnlyList<StagedPath> paths)
    {
        foreach (var path in paths.Reverse())
        {
            if (path.IsDirectory && Directory.Exists(path.TemporaryPath) && !Directory.Exists(path.OriginalPath))
            {
                Directory.Move(path.TemporaryPath, path.OriginalPath);
            }
            else if (!path.IsDirectory && File.Exists(path.TemporaryPath) && !File.Exists(path.OriginalPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path.OriginalPath)!);
                File.Move(path.TemporaryPath, path.OriginalPath, overwrite: true);
            }
        }
    }

    private static void DeleteStagedPaths(IReadOnlyList<StagedPath> paths)
    {
        foreach (var path in paths.OrderByDescending(static path => path.IsDirectory))
        {
            if (path.IsDirectory)
            {
                TryDeleteDirectory(path.TemporaryPath);
            }
            else if (File.Exists(path.TemporaryPath))
            {
                File.Delete(path.TemporaryPath);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record DeletionTarget(string StoredFilePath, int ChapterCount, bool HasReadingProgress);
    private sealed record StagedPath(string OriginalPath, string TemporaryPath, bool IsDirectory);
}
