using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>
/// Reads detached library and book-detail projections from SQLite.
/// </summary>
public sealed class BookLibraryQuery : IBookLibraryQuery
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public BookLibraryQuery(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT b.Id,
                   b.Title,
                   b.Author,
                   COALESCE(
                       (SELECT c.Title
                        FROM ReadingProgress progress
                        INNER JOIN Chapters c
                            ON c.BookId = progress.BookId
                           AND c.ChapterIndex = progress.ChapterIndex
                        WHERE progress.BookId = b.Id
                        ORDER BY progress.UpdatedAt DESC
                        LIMIT 1),
                       (SELECT c.Title
                        FROM Chapters c
                        WHERE c.BookId = b.Id
                        ORDER BY c.SortOrder, c.ChapterIndex
                        LIMIT 1),
                       '未开始') AS CurrentChapterTitle,
                   b.ImportedAt,
                   b.LastPlayedAt,
                   COALESCE(chapterCounts.TotalChapterCount, 0) AS TotalChapterCount,
                   rp.ChapterIndex,
                   CASE WHEN rp.BookId IS NULL THEN 0 ELSE 1 END AS HasReadingProgress
            FROM Books b
            LEFT JOIN (
                SELECT BookId, COUNT(*) AS TotalChapterCount
                FROM Chapters
                GROUP BY BookId
            ) chapterCounts ON chapterCounts.BookId = b.Id
            LEFT JOIN ReadingProgress rp ON rp.BookId = b.Id
            ORDER BY b.ImportedAt DESC, b.Id;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var books = new List<BookSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (TryMapSummary(reader, out var summary))
            {
                books.Add(summary);
            }
        }

        return books;
    }

    public async Task<BookDetailsHeader?> GetBookDetailsHeaderAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, Author FROM Books WHERE Id = $bookId LIMIT 1;";
        command.Parameters.AddWithValue("$bookId", bookId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new BookDetailsHeader(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    }

    public async Task<BookDetails?> GetBookDetailsAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var header = await ReadDetailsHeaderAsync(connection, bookId, cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

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
                header.CurrentChapterIndex == chapterIndex));
        }

        return new BookDetails(
            header.Id,
            header.Title,
            header.Author,
            header.TotalChapterCount,
            header.CurrentChapterIndex,
            header.RemainingChapterCount,
            header.OverallProgress,
            header.HasReadingProgress,
            header.CachedAudioBytes,
            chapters);
    }

    private static bool TryMapSummary(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        out BookSummary summary)
    {
        summary = null!;
        try
        {
            if (!SqliteDateTimeMapper.TryParse(reader.GetString(4), out var importedAt) ||
                (!reader.IsDBNull(5) &&
                 !SqliteDateTimeMapper.TryParse(reader.GetString(5), out _)))
            {
                return false;
            }

            var lastPlayedAt = reader.IsDBNull(5)
                ? (DateTimeOffset?)null
                : SqliteDateTimeMapper.Parse(reader.GetString(5));
            var totalChapterCount = reader.GetInt32(6);
            var currentChapterIndex = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);
            var hasReadingProgress = reader.GetInt64(8) == 1 && currentChapterIndex is not null;
            var clampedIndex = hasReadingProgress && totalChapterCount > 0
                ? Math.Clamp(currentChapterIndex!.Value, 0, totalChapterCount - 1)
                : (int?)null;

            summary = new BookSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                importedAt,
                lastPlayedAt,
                totalChapterCount,
                clampedIndex,
                clampedIndex is null ? totalChapterCount : Math.Max(0, totalChapterCount - clampedIndex.Value - 1),
                clampedIndex is null || totalChapterCount == 0 ? 0 : (double)(clampedIndex.Value + 1) / totalChapterCount,
                hasReadingProgress);
            return true;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static async Task<DetailsHeader?> ReadDetailsHeaderAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string bookId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT b.Id, b.Title, b.Author,
                   COALESCE(chapterCounts.TotalChapterCount, 0),
                   rp.ChapterIndex,
                   CASE WHEN rp.BookId IS NULL THEN 0 ELSE 1 END,
                   COALESCE(cache.TotalSizeBytes, 0)
            FROM Books b
            LEFT JOIN (SELECT BookId, COUNT(*) AS TotalChapterCount FROM Chapters GROUP BY BookId) chapterCounts
                ON chapterCounts.BookId = b.Id
            LEFT JOIN ReadingProgress rp ON rp.BookId = b.Id
            LEFT JOIN (SELECT BookId, COALESCE(SUM(FileSize), 0) AS TotalSizeBytes FROM AudioCacheEntries GROUP BY BookId) cache
                ON cache.BookId = b.Id
            WHERE b.Id = $bookId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$bookId", bookId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var totalChapterCount = reader.GetInt32(3);
        var currentChapterIndex = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
        var hasProgress = reader.GetInt64(5) == 1 && currentChapterIndex is not null;
        var clampedIndex = hasProgress && totalChapterCount > 0
            ? Math.Clamp(currentChapterIndex!.Value, 0, totalChapterCount - 1)
            : (int?)null;

        return new DetailsHeader(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            totalChapterCount,
            clampedIndex,
            clampedIndex is null ? totalChapterCount : Math.Max(0, totalChapterCount - clampedIndex.Value - 1),
            clampedIndex is null || totalChapterCount == 0 ? 0 : (double)(clampedIndex.Value + 1) / totalChapterCount,
            hasProgress,
            reader.GetInt64(6));
    }

    private sealed record DetailsHeader(
        string Id,
        string Title,
        string? Author,
        int TotalChapterCount,
        int? CurrentChapterIndex,
        int RemainingChapterCount,
        double OverallProgress,
        bool HasReadingProgress,
        long CachedAudioBytes);
}
