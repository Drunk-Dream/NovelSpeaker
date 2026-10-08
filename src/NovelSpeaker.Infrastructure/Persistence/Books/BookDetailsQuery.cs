using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>
/// Reads the independent book-details projections from SQLite.
/// </summary>
public sealed class BookDetailsQuery : IBookDetailsQuery
{
    internal const string HeaderSql = """
        SELECT b.Id, b.Title, b.Author, b.Description, b.ActiveSourceId,
            (SELECT Id FROM Chapters WHERE SourceId = b.ActiveSourceId ORDER BY ChapterIndex LIMIT 1),
            s.SourceType, s.Title, s.Author, s.Description
        FROM Books b LEFT JOIN BookSources s ON s.Id = b.ActiveSourceId AND s.BookId = b.Id
        WHERE b.Id = $bookId LIMIT 1;
        """;

    internal const string CatalogSql =
        """
        SELECT c.ChapterIndex, c.Title, c.Id, c.SourceId,
            (SELECT Id FROM Chapters WHERE SourceId = b.ActiveSourceId ORDER BY ChapterIndex LIMIT 1)
        FROM Books b JOIN Chapters c ON c.SourceId = b.ActiveSourceId
        WHERE b.Id = $bookId
        ORDER BY c.SortOrder, c.ChapterIndex;
        """;

    internal const string ReadingPositionSql =
        """
        SELECT rp.BookId, MAX(0, MIN(rp.ChapterIndex, (SELECT MAX(ChapterIndex) FROM Chapters WHERE SourceId = b.ActiveSourceId))),
               rp.SegmentIndex, rp.CharacterOffset, rp.AudioPositionMilliseconds, rp.UpdatedAt
        FROM ReadingProgress rp JOIN Books b ON b.Id = rp.BookId
        WHERE rp.BookId = $bookId AND EXISTS (SELECT 1 FROM Chapters WHERE SourceId = b.ActiveSourceId)
        LIMIT 1;
        """;

    internal const string StatisticsSql =
        """
        SELECT
            (SELECT COUNT(*) FROM Chapters WHERE SourceId = Books.ActiveSourceId),
            COALESCE((SELECT SUM(FileSize) FROM AudioCacheEntries WHERE BookId = $bookId), 0)
        FROM Books
        WHERE Id = $bookId
        LIMIT 1;
        """;

    private readonly ISqliteConnectionFactory _connectionFactory;

    public BookDetailsQuery(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<BookDetailsHeader?> GetHeaderAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = HeaderSql;
        command.Parameters.AddWithValue("$bookId", bookId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new BookDetailsHeader(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : new ActiveSourceSummary(
                    new ActiveSourceContext(reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)),
                    (SourceType)reader.GetInt32(6), reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9)))
            : null;
    }

    public async Task<IReadOnlyList<BookChapterSummary>> GetCatalogAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = CatalogSql;
        command.Parameters.AddWithValue("$bookId", bookId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var catalog = new List<BookChapterSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            catalog.Add(new BookChapterSummary(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                new ActiveSourceContext(reader.GetString(3), reader.GetString(4))));
        }

        return catalog.ToArray();
    }

    public async Task<BookReadingPosition?> GetReadingPositionAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = ReadingPositionSql;
        command.Parameters.AddWithValue("$bookId", bookId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (TryMapReadingPosition(reader, out var position))
            {
                return position;
            }
        }

        return null;
    }

    public async Task<BookDetailsStatistics?> GetStatisticsAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = StatisticsSql;
        command.Parameters.AddWithValue("$bookId", bookId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new BookDetailsStatistics(reader.GetInt64(1))
            : null;
    }

    private static bool TryMapReadingPosition(
        SqliteDataReader reader,
        out BookReadingPosition position)
    {
        position = null!;
        try
        {
            if (!SqliteDateTimeMapper.TryParse(reader.GetString(5), out var updatedAt))
            {
                return false;
            }

            position = new BookReadingPosition(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt64(4),
                updatedAt);
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
}
