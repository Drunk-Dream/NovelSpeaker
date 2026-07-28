using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>
/// Deletes book-owned database rows and staged files with rollback compensation.
/// </summary>
public sealed class BookDeletionOperationStore : IBookDeletionOperationStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IAppDataDirectoryProvider _directories;
    private readonly IAudioCacheProtectionRegistry _audioCacheProtectionRegistry;
    private readonly IAppStoragePathResolver _pathResolver;
    private readonly IBookOperationJournal _operationJournal;
    private readonly TimeProvider _timeProvider;

    public BookDeletionOperationStore(
        ISqliteConnectionFactory connectionFactory,
        IAppDataDirectoryProvider directories,
        IAudioCacheProtectionRegistry audioCacheProtectionRegistry,
        IAppStoragePathResolver pathResolver,
        IBookOperationJournal operationJournal,
        TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _directories = directories;
        _audioCacheProtectionRegistry = audioCacheProtectionRegistry;
        _pathResolver = pathResolver;
        _operationJournal = operationJournal;
        _timeProvider = timeProvider;
    }

    public async Task<BookDeletionPreparation?> BeginAsync(BookDeleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var book = await ReadDeletionTargetAsync(connection, request.BookId, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return null;
        }

        var operationId = Guid.NewGuid().ToString("N");
        var stageRoot = Path.Combine(_directories.RootDirectoryPath, "Operations", operationId);
        var operationPaths = await BuildOperationPathsAsync(
            connection,
            request,
            book,
            stageRoot,
            cancellationToken).ConfigureAwait(false);
        var operation = new BookOperationRecord(
            operationId,
            BookOperationKind.Delete,
            BookOperationPhase.Staged,
            request.BookId,
            operationPaths,
            _timeProvider.GetUtcNow());
        var preparation = new BookDeletionPreparation(
            operationId,
            new BookDeleteResult(request.BookId, request.DeleteAudioCache, book.ChapterCount, book.HasReadingProgress));

        try
        {
            await _operationJournal.CreateAsync(operation, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(stageRoot);
            StagePaths(operationPaths, cancellationToken);
            return preparation;
        }
        catch
        {
            RestoreStagedPaths(operationPaths);
            TryDeleteDirectory(stageRoot);
            throw;
        }
    }

    public async Task CommitAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await DeleteRowsAsync(
            connection,
            new BookDeleteRequest(preparation.Result.BookId, preparation.Result.DeletedAudioCache),
            cancellationToken).ConfigureAwait(false);
        await _operationJournal.SetPhaseAsync(
            preparation.OperationId,
            BookOperationPhase.DatabaseCommitted,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken)
    {
        var operation = await FindOperationAsync(preparation.OperationId, cancellationToken).ConfigureAwait(false);
        DeleteStagedPaths(operation.Paths, cancellationToken);
        TryDeleteDirectory(Path.Combine(_directories.OperationsDirectoryPath, preparation.OperationId));
        await _operationJournal.SetPhaseAsync(
            preparation.OperationId,
            BookOperationPhase.Completed,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RollbackAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken)
    {
        var operation = await FindOperationAsync(preparation.OperationId, cancellationToken).ConfigureAwait(false);
        if (!await BookExistsAsync(operation.BookId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        RestoreStagedPaths(operation.Paths);
        TryDeleteDirectory(Path.Combine(_directories.OperationsDirectoryPath, preparation.OperationId));
        await _operationJournal.SetPhaseAsync(
            preparation.OperationId,
            BookOperationPhase.Completed,
            cancellationToken).ConfigureAwait(false);
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
            // The database rows are book-owned even when the user chose to retain the
            // physical cache files for later orphan maintenance.
            await ExecuteDeleteAsync(
                connection,
                transaction,
                "DELETE FROM AudioCacheEntries WHERE BookId = $bookId;",
                request.BookId,
                cancellationToken).ConfigureAwait(false);

            await ExecuteDeleteAsync(
                connection,
                transaction,
                """
                DELETE FROM ChapterSpeechPlanSegments
                WHERE ChapterId IN (SELECT Id FROM Chapters WHERE BookId = $bookId);
                """,
                request.BookId,
                cancellationToken).ConfigureAwait(false);
            await ExecuteDeleteAsync(
                connection,
                transaction,
                """
                DELETE FROM ChapterSpeechPlans
                WHERE ChapterId IN (SELECT Id FROM Chapters WHERE BookId = $bookId);
                """,
                request.BookId,
                cancellationToken).ConfigureAwait(false);

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

    private async Task<IReadOnlyList<BookOperationPath>> BuildOperationPathsAsync(
        SqliteConnection connection,
        BookDeleteRequest request,
        DeletionTarget book,
        string stageRoot,
        CancellationToken cancellationToken)
    {
        var paths = new List<BookOperationPath>();
        var storedFilePath = _pathResolver.ResolvePath(book.StoredFilePath);
        var bookDirectory = Path.GetDirectoryName(storedFilePath)
            ?? throw new InvalidDataException("书籍正文路径缺少所属目录。");
        var expectedBookDirectory = Path.GetFullPath(Path.Combine(_directories.BooksDirectoryPath, request.BookId));
        if (!string.Equals(bookDirectory, expectedBookDirectory, PathComparison))
        {
            throw new InvalidDataException("书籍正文路径不属于对应的应用内书籍目录。");
        }

        paths.Add(new BookOperationPath(
            _pathResolver.GetStorageKey(bookDirectory),
            _pathResolver.GetStorageKey(Path.Combine(stageRoot, "book")),
            IsDirectory: true));

        if (!request.DeleteAudioCache)
        {
            return paths;
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT FilePath FROM AudioCacheEntries WHERE BookId = $bookId ORDER BY CacheKey;";
        command.Parameters.AddWithValue("$bookId", request.BookId);

        var index = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var filePath = _pathResolver.ResolvePath(reader.GetString(0));
            EnsureCachePath(filePath);
            if (_audioCacheProtectionRegistry.IsProtected(filePath))
            {
                throw new InvalidOperationException("无法删除当前仍受保护的缓存文件。");
            }

            paths.Add(new BookOperationPath(
                _pathResolver.GetStorageKey(filePath),
                _pathResolver.GetStorageKey(Path.Combine(
                    stageRoot,
                    "cache",
                    $"{index++:D8}{Path.GetExtension(filePath)}")),
                IsDirectory: false));
        }

        return paths;
    }

    private void StagePaths(IReadOnlyList<BookOperationPath> paths, CancellationToken cancellationToken)
    {
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originalPath = _pathResolver.ResolvePath(path.OriginalStorageKey);
            var stagedPath = _pathResolver.ResolvePath(path.StagedStorageKey);
            if (path.IsDirectory)
            {
                if (!Directory.Exists(originalPath))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                Directory.Move(originalPath, stagedPath);
            }
            else if (File.Exists(originalPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                File.Move(originalPath, stagedPath);
            }
        }
    }

    private void RestoreStagedPaths(IReadOnlyList<BookOperationPath> paths)
    {
        foreach (var path in paths.Reverse())
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
    }

    private void DeleteStagedPaths(IReadOnlyList<BookOperationPath> paths, CancellationToken cancellationToken)
    {
        foreach (var path in paths.OrderByDescending(static path => path.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stagedPath = _pathResolver.ResolvePath(path.StagedStorageKey);
            if (path.IsDirectory)
            {
                TryDeleteDirectory(stagedPath);
            }
            else if (File.Exists(stagedPath))
            {
                File.Delete(stagedPath);
            }
        }
    }

    private async Task<BookOperationRecord> FindOperationAsync(string operationId, CancellationToken cancellationToken)
    {
        var operations = await _operationJournal.GetIncompleteAsync(cancellationToken).ConfigureAwait(false);
        return operations.Single(operation => string.Equals(operation.OperationId, operationId, StringComparison.Ordinal));
    }

    private async Task<bool> BookExistsAsync(string bookId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Books WHERE Id = $bookId);";
        command.Parameters.AddWithValue("$bookId", bookId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private void EnsureCachePath(string filePath)
    {
        var cacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_directories.CacheDirectoryPath));
        var cachePrefix = cacheRoot + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(cachePrefix, PathComparison))
        {
            throw new InvalidDataException("音频缓存路径不属于应用缓存目录。");
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

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
