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
                       (SELECT Title
                        FROM Chapters c
                        WHERE c.BookId = b.Id
                        ORDER BY c.ChapterIndex
                        LIMIT 1),
                       '未开始') AS CurrentChapterTitle,
                   b.ImportedAt
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
                reader.GetString(4)));
        }

        return books;
    }
}
