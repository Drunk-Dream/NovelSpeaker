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
        if (operation.Phase == BookOperationPhase.Staged && await TargetExistsAsync(operation, cancellationToken).ConfigureAwait(false))
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
                // Once SQLite committed, a later import may own the original location again.
                // Only the immutable staged files belong to this deletion operation.
                DeleteStaged(path);
            }
        }

        if (Directory.Exists(operationDirectory))
        {
            BookOperationFileTrust.VerifyTree(operationDirectory, _pathResolver, cancellationToken);
            Directory.Delete(operationDirectory, recursive: true);
        }

        await CompleteAsync(operation.OperationId, cancellationToken).ConfigureAwait(false);
    }

    private async Task CompleteAsync(string operationId, CancellationToken cancellationToken)
    {
        await _journal.SetPhaseAsync(operationId, BookOperationPhase.Completed, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TargetExistsAsync(BookOperationRecord operation, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = operation.Kind == BookOperationKind.RemoveSource
            ? "SELECT EXISTS(SELECT 1 FROM BookSources WHERE Id = $sourceId AND BookId = $bookId);"
            : "SELECT EXISTS(SELECT 1 FROM Books WHERE Id = $bookId);";
        command.Parameters.AddWithValue("$bookId", operation.BookId);
        command.Parameters.AddWithValue("$sourceId", (object?)operation.SourceId ?? DBNull.Value);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
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
            BookOperationFileTrust.VerifyTree(stagedPath, _pathResolver, CancellationToken.None);
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
        var expectedBookDirectory = _pathResolver.ResolvePath(Path.Combine(_directories.BooksDirectoryPath, operation.BookId));
        if (!PathEquals(Path.GetDirectoryName(expectedBookDirectory)!, _directories.BooksDirectoryPath))
            throw new InvalidDataException("删除恢复记录的书籍目录不属于书籍根目录。");
        var operationStageRoot = Path.Combine(_directories.OperationsDirectoryPath, operation.OperationId);
        var resolvedOperationDirectory = _pathResolver.ResolvePath(operationStageRoot);
        if (!PathEquals(Path.GetDirectoryName(resolvedOperationDirectory)!, _directories.OperationsDirectoryPath))
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
                ? operation.Kind == BookOperationKind.Delete && PathEquals(originalPath, expectedBookDirectory)
                : IsDescendant(originalPath, _directories.CacheDirectoryPath) ||
                    (operation.Kind == BookOperationKind.RemoveSource && PathEquals(Path.GetDirectoryName(originalPath)!, expectedBookDirectory));
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
