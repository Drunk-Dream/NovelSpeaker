using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;
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
        => await BeginCoreAsync(request, null, cancellationToken).ConfigureAwait(false);

    public async Task<BookDeletionPreparation?> BeginSourceRemovalAsync(BookSourceRemoveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceId);
        return await BeginCoreAsync(new BookDeleteRequest(request.BookId, true), request.SourceId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BookDeletionPreparation?> BeginCoreAsync(BookDeleteRequest request, string? sourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var book = await ReadDeletionTargetAsync(connection, request.BookId, sourceId, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return null;
        }

        var operationId = Guid.NewGuid().ToString("N");
        var stageRoot = _pathResolver.ResolvePath(Path.Combine(_directories.OperationsDirectoryPath, operationId));
        var operationPaths = await BuildOperationPathsAsync(
            connection,
            request,
            book,
            sourceId,
            stageRoot,
            cancellationToken).ConfigureAwait(false);
        var operation = new BookOperationRecord(
            operationId,
            book.DeletesBook ? BookOperationKind.Delete : BookOperationKind.RemoveSource,
            BookOperationPhase.Staged,
            request.BookId,
            operationPaths,
            _timeProvider.GetUtcNow(),
            book.DeletesBook ? null : sourceId);
        var preparation = new BookDeletionPreparation(
            operationId,
            new BookDeleteResult(request.BookId, request.DeleteAudioCache, book.ChapterCount, book.DeletesBook && book.HasReadingProgress),
            book.DeletesBook, book.ActiveSourceId == sourceId || book.DeletesBook ? book.ActiveSourceId : null);

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
        var operation = await FindOperationAsync(preparation.OperationId, cancellationToken).ConfigureAwait(false);
        await DeleteRowsAsync(
            connection,
            new BookDeleteRequest(preparation.Result.BookId, preparation.Result.DeletedAudioCache),
            operation.SourceId,
            preparation.OperationId,
            _timeProvider.GetUtcNow(),
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
        if (operation.Phase != BookOperationPhase.Staged || !await TargetExistsAsync(operation, cancellationToken).ConfigureAwait(false))
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
        string? sourceId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT (SELECT COUNT(*) FROM Chapters c JOIN BookSources s ON s.Id = c.SourceId
                    WHERE s.BookId = b.Id AND ($sourceId IS NULL OR s.Id = $sourceId)),
                   CASE WHEN EXISTS (SELECT 1 FROM ReadingProgress rp WHERE rp.BookId = b.Id) THEN 1 ELSE 0 END,
                   b.ActiveSourceId,
                   CASE WHEN $sourceId IS NULL OR (SELECT COUNT(*) FROM BookSources s WHERE s.BookId = b.Id) = 1 THEN 1 ELSE 0 END
            FROM Books b
            WHERE b.Id = $bookId AND ($sourceId IS NULL OR EXISTS (SELECT 1 FROM BookSources s WHERE s.BookId = b.Id AND s.Id = $sourceId))
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$bookId", bookId);
        command.Parameters.AddWithValue("$sourceId", (object?)sourceId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new DeletionTarget(reader.GetInt32(0), reader.GetInt64(1) == 1,
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt64(3) == 1)
            : null;
    }

    private static async Task DeleteRowsAsync(
        SqliteConnection connection,
        BookDeleteRequest request,
        string? sourceId,
        string operationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Foreign keys clean relational descendants; staged paths handle physical files.
            using var target = connection.CreateCommand();
            target.Transaction = transaction;
            target.CommandText = sourceId is null ? "DELETE FROM Books WHERE Id = $bookId;"
                : "DELETE FROM BookSources WHERE Id = $sourceId AND BookId = $bookId;";
            target.Parameters.AddWithValue("$bookId", request.BookId);
            target.Parameters.AddWithValue("$sourceId", (object?)sourceId ?? DBNull.Value);
            if (await target.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The staged removal target no longer exists.");

            using var journal = connection.CreateCommand();
            journal.Transaction = transaction;
            journal.CommandText = "UPDATE BookOperations SET Phase = 'DatabaseCommitted', UpdatedAt = $now WHERE OperationId = $operationId AND Phase = 'Staged';";
            journal.Parameters.AddWithValue("$operationId", operationId);
            journal.Parameters.AddWithValue("$now", SqliteDateTimeMapper.Format(now));
            if (await journal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The staged removal operation is missing.");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IReadOnlyList<BookOperationPath>> BuildOperationPathsAsync(
        SqliteConnection connection,
        BookDeleteRequest request,
        DeletionTarget book,
        string? sourceId,
        string stageRoot,
        CancellationToken cancellationToken)
    {
        var paths = new List<BookOperationPath>();
        var bookDirectory = _pathResolver.ResolvePath(Path.Combine(_directories.BooksDirectoryPath, request.BookId));
        if (!bookDirectory.StartsWith(Path.TrimEndingDirectorySeparator(_directories.BooksDirectoryPath) + Path.DirectorySeparatorChar, PathComparison) ||
            !string.Equals(Path.GetDirectoryName(bookDirectory), Path.GetFullPath(_directories.BooksDirectoryPath), PathComparison))
        {
            throw new InvalidDataException("书籍目录不属于应用内书籍根目录。");
        }

        using (var content = connection.CreateCommand())
        {
            content.CommandText = "SELECT l.StoredContentPath FROM LocalBookSources l JOIN BookSources s ON s.Id = l.SourceId WHERE s.BookId = $bookId AND ($sourceId IS NULL OR s.Id = $sourceId);";
            content.Parameters.AddWithValue("$bookId", request.BookId);
            content.Parameters.AddWithValue("$sourceId", (object?)(book.DeletesBook ? null : sourceId) ?? DBNull.Value);
            await using var contentReader = await content.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await contentReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var storedPath = _pathResolver.ResolvePath(contentReader.GetString(0));
                if (!string.Equals(Path.GetDirectoryName(storedPath), bookDirectory, PathComparison))
                    throw new InvalidDataException("Local Source 正文路径不属于对应书籍目录。");
                if (!book.DeletesBook)
                    paths.Add(new BookOperationPath(_pathResolver.GetStorageKey(storedPath),
                        _pathResolver.GetStorageKey(Path.Combine(stageRoot, "content", Path.GetFileName(storedPath))), false));
            }
        }
        if (book.DeletesBook)
            paths.Add(new BookOperationPath(_pathResolver.GetStorageKey(bookDirectory),
                _pathResolver.GetStorageKey(Path.Combine(stageRoot, "book")), true));

        if (!request.DeleteAudioCache)
        {
            return paths;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FilePath FROM AudioCacheEntries WHERE BookId = $bookId AND ($sourceId IS NULL OR ChapterId IN (SELECT Id FROM Chapters WHERE SourceId = $sourceId)) ORDER BY CacheKey;";
        command.Parameters.AddWithValue("$bookId", request.BookId);
        command.Parameters.AddWithValue("$sourceId", (object?)(book.DeletesBook ? null : sourceId) ?? DBNull.Value);

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
                BookOperationFileTrust.VerifyTree(originalPath, _pathResolver, cancellationToken);
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

    private async Task<bool> TargetExistsAsync(BookOperationRecord operation, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = operation.SourceId is null ? "SELECT EXISTS(SELECT 1 FROM Books WHERE Id = $bookId);"
            : "SELECT EXISTS(SELECT 1 FROM BookSources WHERE Id = $sourceId AND BookId = $bookId);";
        command.Parameters.AddWithValue("$bookId", operation.BookId);
        command.Parameters.AddWithValue("$sourceId", (object?)operation.SourceId ?? DBNull.Value);
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

    private void TryDeleteDirectory(string path)
    {
        BookOperationFileTrust.VerifyTree(path, _pathResolver, CancellationToken.None);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record DeletionTarget(int ChapterCount, bool HasReadingProgress, string? ActiveSourceId, bool DeletesBook);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
