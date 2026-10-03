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
        if (operation.Paths.Count is < 1 or > 2 || operation.Paths.Any(path => path.IsDirectory))
        {
            throw new InvalidDataException("导入恢复记录的路径集合无效。");
        }

        for (var index = 0; index < operation.Paths.Count; index++)
        {
            ValidateImportPath(operation.BookId, operation.Paths[index], isNewSnapshot: index == 0);
        }

        var path = operation.Paths[0];
        var finalPath = _pathResolver.ResolvePath(path.OriginalStorageKey);
        var stagedPath = _pathResolver.ResolvePath(path.StagedStorageKey);
        var referenced = await ContentPathIsReferencedAsync(operation.BookId, path.OriginalStorageKey, cancellationToken).ConfigureAwait(false);
        if (!referenced)
        {
            DeleteFile(stagedPath);
            DeleteFile(finalPath);
            TryDeleteEmptyParent(finalPath);
        }
        else if (File.Exists(finalPath))
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
            throw new InvalidDataException("已提交的 Local Source 正文快照缺失。");
        }

        if (operation.Paths.Count == 2)
        {
            var oldPath = operation.Paths[1];
            if (!await ContentPathIsReferencedAsync(operation.BookId, oldPath.OriginalStorageKey, cancellationToken).ConfigureAwait(false))
            {
                DeleteFile(_pathResolver.ResolvePath(oldPath.OriginalStorageKey));
            }
        }

        await CompleteAsync(operation.OperationId, cancellationToken).ConfigureAwait(false);
    }

    private void ValidateImportPath(string bookId, BookOperationPath path, bool isNewSnapshot)
    {
        var original = _pathResolver.ResolvePath(path.OriginalStorageKey);
        var staged = _pathResolver.ResolvePath(path.StagedStorageKey);
        var directory = _pathResolver.ResolvePath(Path.Combine(_directories.BooksDirectoryPath, bookId));
        var name = Path.GetFileName(original);
        var validName = name == "content.txt" ||
            (name.StartsWith("content-", StringComparison.Ordinal) && name.EndsWith(".txt", StringComparison.Ordinal) &&
             Guid.TryParseExact(name[8..^4], "N", out _));
        if (!PathEquals(Path.GetDirectoryName(original)!, directory) || !validName ||
            !PathEquals(staged, isNewSnapshot ? original + ".tmp" : original))
        {
            throw new InvalidDataException("导入恢复记录包含不属于目标书籍的路径。");
        }
    }

    private async Task<bool> ContentPathIsReferencedAsync(string bookId, string path, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.StoredContentPath FROM LocalBookSources l
            JOIN BookSources s ON s.Id = l.SourceId WHERE s.BookId = $bookId;
            """;
        command.Parameters.AddWithValue("$bookId", bookId);
        var current = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        // A legacy journal can still contain absolute paths after typed storage has been normalized.
        return current is not null && PathEquals(_pathResolver.ResolvePath(current), _pathResolver.ResolvePath(path));
    }

    private async Task RecoverDeleteAsync(BookOperationRecord operation, CancellationToken cancellationToken)
    {
        var operationDirectory = ValidateDeletePaths(operation);
        if (await BookExistsAsync(operation.BookId, cancellationToken).ConfigureAwait(false))
        {
            foreach (var path in operation.Paths.Reverse())
            {
                Restore(path);
            }
        }
        else
        {
            await DeleteBookRowsAsync(operation.BookId, cancellationToken).ConfigureAwait(false);
            foreach (var path in operation.Paths)
            {
                DeleteOriginal(path);
                DeleteStaged(path);
            }
        }

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
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Books WHERE Id = $bookId);";
        command.Parameters.AddWithValue("$bookId", bookId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private async Task DeleteBookRowsAsync(string bookId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        using var cache = connection.CreateCommand();
        cache.Transaction = transaction;
        cache.CommandText = "DELETE FROM AudioCacheEntries WHERE BookId = $bookId;";
        cache.Parameters.AddWithValue("$bookId", bookId);
        await cache.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        using var segments = connection.CreateCommand();
        segments.Transaction = transaction;
        segments.CommandText =
            """
            DELETE FROM ChapterSpeechPlanSegments
            WHERE ChapterId IN (SELECT Id FROM Chapters WHERE BookId = $bookId);
            """;
        segments.Parameters.AddWithValue("$bookId", bookId);
        await segments.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        using var plans = connection.CreateCommand();
        plans.Transaction = transaction;
        plans.CommandText =
            """
            DELETE FROM ChapterSpeechPlans
            WHERE ChapterId IN (SELECT Id FROM Chapters WHERE BookId = $bookId);
            """;
        plans.Parameters.AddWithValue("$bookId", bookId);
        await plans.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        using var progress = connection.CreateCommand();
        progress.Transaction = transaction;
        progress.CommandText = "DELETE FROM ReadingProgress WHERE BookId = $bookId;";
        progress.Parameters.AddWithValue("$bookId", bookId);
        await progress.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        using var chapters = connection.CreateCommand();
        chapters.Transaction = transaction;
        chapters.CommandText = "DELETE FROM Chapters WHERE BookId = $bookId;";
        chapters.Parameters.AddWithValue("$bookId", bookId);
        await chapters.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        using var book = connection.CreateCommand();
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

    private void DeleteOriginal(BookOperationPath path)
    {
        var originalPath = _pathResolver.ResolvePath(path.OriginalStorageKey);
        if (path.IsDirectory && Directory.Exists(originalPath))
        {
            Directory.Delete(originalPath, recursive: true);
        }
        else if (!path.IsDirectory)
        {
            DeleteFile(originalPath);
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

    private string ValidateDeletePaths(BookOperationRecord operation)
    {
        var expectedBookDirectory = Path.Combine(_directories.BooksDirectoryPath, operation.BookId);
        var operationStageRoot = Path.Combine(_directories.OperationsDirectoryPath, operation.OperationId);
        var resolvedOperationDirectory = _pathResolver.ResolvePath(operationStageRoot);
        if (!IsDescendant(resolvedOperationDirectory, _directories.OperationsDirectoryPath))
        {
            throw new InvalidDataException("删除恢复记录的操作目录不属于应用操作目录。");
        }

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

        return resolvedOperationDirectory;
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
