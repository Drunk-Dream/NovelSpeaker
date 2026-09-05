using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>
/// Reads the independent book-details projections from SQLite.
/// </summary>
public sealed class BookDetailsQuery : IBookDetailsQuery
{
    internal const string HeaderSql = "SELECT Id, Title, Author FROM Books WHERE Id = $bookId LIMIT 1;";

    internal const string CatalogSql =
        """
        SELECT ChapterIndex, Title, StartOffset, Length
        FROM Chapters
        WHERE BookId = $bookId
        ORDER BY SortOrder, ChapterIndex;
        """;

    internal const string ReadingPositionSql =
        """
        SELECT BookId, ChapterIndex, SegmentIndex, CharacterOffset, AudioPositionMilliseconds, UpdatedAt
        FROM ReadingProgress
        WHERE BookId = $bookId
        LIMIT 1;
        """;

    internal const string StatisticsSql =
        """
        SELECT
            (SELECT COUNT(*) FROM Chapters WHERE BookId = $bookId),
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
                reader.IsDBNull(2) ? null : reader.GetString(2))
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
                reader.GetInt32(2),
                reader.GetInt32(3)));
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
