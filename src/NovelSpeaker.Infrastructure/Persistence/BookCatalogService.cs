using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Reads lightweight book rows for the library page.
/// </summary>
public sealed class BookCatalogService : IBookCatalogService
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public BookCatalogService(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT b.Id,
                   b.Title,
                   b.Author,
                   COALESCE(
                       (SELECT c.Title
                        FROM ReadingProgress rp
                        INNER JOIN Chapters c
                            ON c.BookId = rp.BookId
                           AND c.ChapterIndex = rp.ChapterIndex
                        WHERE rp.BookId = b.Id
                        ORDER BY rp.UpdatedAt DESC
                        LIMIT 1),
                       (SELECT Title
                        FROM Chapters c
                        WHERE c.BookId = b.Id
                        ORDER BY c.SortOrder, c.ChapterIndex
                        LIMIT 1),
                       '未开始') AS CurrentChapterTitle,
                   b.ImportedAt,
                   b.LastPlayedAt
            FROM Books b
            ORDER BY b.ImportedAt DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var books = new List<BookSummary>();

        while (await reader.ReadAsync(cancellationToken))
        {
            books.Add(new BookSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return books;
    }

    public async Task<ContinueListeningSummary?> GetContinueListeningAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT b.Id,
                   b.Title,
                   COALESCE(c.Title, '未开始') AS ChapterTitle,
                   b.LastPlayedAt,
                   rp.ChapterIndex,
                   rp.SegmentIndex
            FROM Books b
            INNER JOIN ReadingProgress rp ON rp.BookId = b.Id
            LEFT JOIN Chapters c
                ON c.BookId = rp.BookId
               AND c.ChapterIndex = rp.ChapterIndex
            WHERE b.LastPlayedAt IS NOT NULL
            ORDER BY b.LastPlayedAt DESC, rp.UpdatedAt DESC
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ContinueListeningSummary(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5));
    }
}
