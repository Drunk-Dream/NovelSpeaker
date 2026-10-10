using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>Reads the active source catalog without exposing local content storage.</summary>
public sealed class SqliteBookPlaybackMetadataQuery(ISqliteConnectionFactory connectionFactory) : IBookPlaybackMetadataQuery
{
    private const string HeaderColumns = """
        b.Id, b.Title, b.Author, b.ActiveSourceBindingId,
        (SELECT Id FROM Chapters WHERE BookId = b.Id ORDER BY ChapterIndex LIMIT 1)
        """;

    public async Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        // Header and catalog describe the same committed snapshot.
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {HeaderColumns}, c.ChapterIndex, c.Title, c.Id
            FROM Books b LEFT JOIN Chapters c ON c.BookId = b.Id AND c.SourceBindingId = b.ActiveSourceBindingId
            WHERE b.Id = $id ORDER BY c.SortOrder, c.ChapterIndex;
            """;
        command.Parameters.AddWithValue("$id", bookId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var header = ReadHeader(reader);
        var chapters = new List<PlaybackChapterSummaryMetadata>();
        do
        {
            if (!reader.IsDBNull(5))
                chapters.Add(new(reader.GetInt32(5), reader.GetString(6), reader.GetString(7)));
        } while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));
        return new(header.BookId, header.Title, header.Author, chapters, header.SourceContext);
    }

    public async Task<PlaybackBookHeader?> GetBookHeaderAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {HeaderColumns} FROM Books b WHERE b.Id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", bookId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadHeader(reader) : null;
    }

    public async Task<PlaybackChapterMetadata?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) =>
        (await GetChaptersAsync(bookId, [chapterIndex], cancellationToken).ConfigureAwait(false)).SingleOrDefault();

    public async Task<IReadOnlyList<PlaybackChapterMetadata>> GetChaptersAsync(
        string bookId, IReadOnlyCollection<int> chapterIndices, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);
        var indices = chapterIndices.Distinct().Order().ToArray();
        if (indices.Length == 0) return [];
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var chapters = new List<(PlaybackChapterMetadata Chapter, int SortOrder)>(indices.Length);
        for (var offset = 0; offset < indices.Length; offset += 400)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("$book", bookId);
            var parameters = indices.Skip(offset).Take(400).Select((value, index) =>
            {
                var name = $"$index{index}";
                command.Parameters.AddWithValue(name, value);
                return name;
            }).ToArray();
            command.CommandText = $"""
                SELECT c.ChapterIndex, c.Title, c.SourceBindingId, c.Id, c.SortOrder,
                    (SELECT Id FROM Chapters WHERE BookId = b.Id ORDER BY ChapterIndex LIMIT 1)
                FROM Books b JOIN Chapters c ON c.BookId = b.Id AND c.SourceBindingId = b.ActiveSourceBindingId
                WHERE b.Id = $book AND c.ChapterIndex IN ({string.Join(", ", parameters)})
                ORDER BY c.SortOrder, c.ChapterIndex;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                chapters.Add((new(bookId, reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    new ActiveSourceContext(reader.GetString(2), reader.GetString(5))), reader.GetInt32(4)));
        }
        transaction.Commit();
        return chapters.OrderBy(row => row.SortOrder).ThenBy(row => row.Chapter.ChapterIndex).Select(row => row.Chapter).ToArray();
    }

    private static PlaybackBookHeader ReadHeader(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : new ActiveSourceContext(reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
}
