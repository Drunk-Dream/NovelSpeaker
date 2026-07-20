using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>
/// Replays incomplete import and deletion records to one idempotent database/file outcome.
/// </summary>
public sealed class BookOperationRecoveryService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IBookOperationJournal _journal;
    private readonly IAppStoragePathResolver _pathResolver;
    private readonly IAppDataDirectoryProvider _directories;

    public BookOperationRecoveryService(
        ISqliteConnectionFactory connectionFactory,
        IBookOperationJournal journal,
        IAppStoragePathResolver pathResolver,
        IAppDataDirectoryProvider directories)
    {
        _connectionFactory = connectionFactory;
        _journal = journal;
        _pathResolver = pathResolver;
        _directories = directories;
    }

    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var operations = await _journal.GetIncompleteAsync(cancellationToken).ConfigureAwait(false);
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.Kind == BookOperationKind.Import)
            {
                await RecoverImportAsync(operation, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RecoverDeleteAsync(operation, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RecoverImportAsync(BookOperationRecord operation, CancellationToken cancellationToken)
    {
        var path = operation.Paths.Single();
        var finalPath = _pathResolver.ResolvePath(path.OriginalStorageKey);
        var stagedPath = _pathResolver.ResolvePath(path.StagedStorageKey);
        var expectedFinalPath = Path.Combine(_directories.BooksDirectoryPath, operation.BookId, "content.txt");
        var expectedStagedPath = expectedFinalPath + ".tmp";
        if (!PathEquals(finalPath, expectedFinalPath) || !PathEquals(stagedPath, expectedStagedPath))
        {
            throw new InvalidDataException("导入恢复记录包含不属于目标书籍的路径。");
        }

        var bookExists = await BookExistsAsync(operation.BookId, cancellationToken).ConfigureAwait(false);

        if (!bookExists)
        {
            DeleteFile(stagedPath);
            DeleteFile(finalPath);
            TryDeleteEmptyParent(finalPath);
            await CompleteAsync(operation.OperationId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (File.Exists(finalPath))
        {
            DeleteFile(stagedPath);
        }
        else if (File.Exists(stagedPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            File.Move(stagedPath, finalPath);
        }
        else
        {
            await DeleteBookRowsAsync(operation.BookId, cancellationToken).ConfigureAwait(false);
            TryDeleteEmptyParent(finalPath);
        }

        await CompleteAsync(operation.OperationId, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecoverDeleteAsync(BookOperationRecord operation, CancellationToken cancellationToken)
    {
        ValidateDeletePaths(operation);
        if (await BookExistsAsync(operation.BookId, cancellationToken).ConfigureAwait(false))
        {
            foreach (var path in operation.Paths.Reverse())
            {
                Restore(path);
            }
        }
        else
        {
            foreach (var path in operation.Paths)
            {
                DeleteStaged(path);
            }
        }

        var operationDirectory = Path.Combine(_directories.OperationsDirectoryPath, operation.OperationId);
        if (Directory.Exists(operationDirectory))
        {
            Directory.Delete(operationDirectory, recursive: true);
        }

        await CompleteAsync(operation.OperationId, cancellationToken).ConfigureAwait(false);
    }

    private async Task CompleteAsync(string operationId, CancellationToken cancellationToken)
    {
        await _journal.SetPhaseAsync(operationId, BookOperationPhase.Completed, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> BookExistsAsync(string bookId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Books WHERE Id = $bookId);";
        command.Parameters.AddWithValue("$bookId", bookId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private async Task DeleteBookRowsAsync(string bookId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var cache = connection.CreateCommand();
        cache.Transaction = transaction;
        cache.CommandText = "DELETE FROM AudioCacheEntries WHERE BookId = $bookId;";
        cache.Parameters.AddWithValue("$bookId", bookId);
        await cache.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var book = connection.CreateCommand();
        book.Transaction = transaction;
        book.CommandText = "DELETE FROM Books WHERE Id = $bookId;";
        book.Parameters.AddWithValue("$bookId", bookId);
        await book.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Restore(BookOperationPath path)
    {
        var originalPath = _pathResolver.ResolvePath(path.OriginalStorageKey);
        var stagedPath = _pathResolver.ResolvePath(path.StagedStorageKey);
        if (path.IsDirectory && Directory.Exists(stagedPath) && !Directory.Exists(originalPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
            Directory.Move(stagedPath, originalPath);
        }
        else if (!path.IsDirectory && File.Exists(stagedPath) && !File.Exists(originalPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
            File.Move(stagedPath, originalPath);
        }
    }

    private void DeleteStaged(BookOperationPath path)
    {
        var stagedPath = _pathResolver.ResolvePath(path.StagedStorageKey);
        if (path.IsDirectory && Directory.Exists(stagedPath))
        {
            Directory.Delete(stagedPath, recursive: true);
        }
        else if (!path.IsDirectory)
        {
            DeleteFile(stagedPath);
        }
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteEmptyParent(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) &&
            Directory.Exists(directory) &&
            !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private void ValidateDeletePaths(BookOperationRecord operation)
    {
        var expectedBookDirectory = Path.Combine(_directories.BooksDirectoryPath, operation.BookId);
        var operationStageRoot = Path.Combine(_directories.OperationsDirectoryPath, operation.OperationId);
        foreach (var path in operation.Paths)
        {
            var originalPath = _pathResolver.ResolvePath(path.OriginalStorageKey);
            var stagedPath = _pathResolver.ResolvePath(path.StagedStorageKey);
            if (!IsDescendant(stagedPath, operationStageRoot))
            {
                throw new InvalidDataException("删除恢复记录的暂存路径不属于对应操作目录。");
            }

            var validOriginal = path.IsDirectory
                ? PathEquals(originalPath, expectedBookDirectory)
                : IsDescendant(originalPath, _directories.CacheDirectoryPath);
            if (!validOriginal)
            {
                throw new InvalidDataException("删除恢复记录包含不属于目标书籍或缓存目录的路径。");
            }
        }
    }

    private static bool IsDescendant(string path, string root)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var prefix = canonicalRoot + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, PathComparison);
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
