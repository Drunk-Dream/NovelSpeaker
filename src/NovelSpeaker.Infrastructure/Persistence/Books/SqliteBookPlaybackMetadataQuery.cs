using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>
/// Reads playback book and chapter metadata without accessing stored chapter text.
/// </summary>
public sealed class SqliteBookPlaybackMetadataQuery : IBookPlaybackMetadataQuery
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteBookPlaybackMetadataQuery(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var bookCommand = connection.CreateCommand();
        bookCommand.CommandText =
            """
            SELECT Id, Title, Author
            FROM Books
            WHERE Id = $id;
            """;
        bookCommand.Parameters.AddWithValue("$id", bookId);

        string title;
        string? author;
        await using (var reader = await bookCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            title = reader.GetString(1);
            author = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        var chapterCommand = connection.CreateCommand();
        chapterCommand.CommandText =
            """
            SELECT ChapterIndex, Title
            FROM Chapters
            WHERE BookId = $bookId
            ORDER BY SortOrder, ChapterIndex;
            """;
        chapterCommand.Parameters.AddWithValue("$bookId", bookId);

        var chapters = new List<PlaybackChapterSummaryMetadata>();
        await using var chapterReader = await chapterCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await chapterReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            chapters.Add(new PlaybackChapterSummaryMetadata(
                chapterReader.GetInt32(0),
                chapterReader.GetString(1)));
        }

        return new PlaybackBookMetadata(bookId, title, author, chapters);
    }

    public async Task<PlaybackChapterMetadata?> GetChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.Id, c.ChapterIndex, c.Title, b.StoredFilePath, c.StartOffset, c.Length
            FROM Chapters c
            INNER JOIN Books b ON b.Id = c.BookId
            WHERE c.BookId = $bookId AND c.ChapterIndex = $chapterIndex
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$bookId", bookId);
        command.Parameters.AddWithValue("$chapterIndex", chapterIndex);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new PlaybackChapterMetadata(
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetString(0))
            : null;
    }

    public async Task<IReadOnlyList<PlaybackChapterMetadata>> GetChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);

        var requestedIndices = chapterIndices
            .Distinct()
            .Order()
            .ToArray();
        if (requestedIndices.Length == 0)
        {
            return [];
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var chapters = new List<ChapterMetadataRow>(requestedIndices.Length);
        const int batchSize = 400;
        for (var offset = 0; offset < requestedIndices.Length; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchCount = Math.Min(batchSize, requestedIndices.Length - offset);
            using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("$bookId", bookId);
            var chapterParameters = new string[batchCount];
            for (var index = 0; index < batchCount; index++)
            {
                var parameterName = $"$chapterIndex{index}";
                chapterParameters[index] = parameterName;
                command.Parameters.AddWithValue(parameterName, requestedIndices[offset + index]);
            }

            command.CommandText =
                $"""
                SELECT c.Id, c.ChapterIndex, c.Title, b.StoredFilePath, c.StartOffset, c.Length, c.SortOrder
                FROM Chapters c
                INNER JOIN Books b ON b.Id = c.BookId
                WHERE c.BookId = $bookId
                  AND c.ChapterIndex IN ({string.Join(", ", chapterParameters)})
                ORDER BY c.SortOrder, c.ChapterIndex;
                """;

            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                chapters.Add(new ChapterMetadataRow(
                    new PlaybackChapterMetadata(
                        reader.GetInt32(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetInt32(4),
                        reader.GetInt32(5),
                        reader.GetString(0)),
                    reader.GetInt32(6)));
            }
        }

        return chapters
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Metadata.ChapterIndex)
            .Select(row => row.Metadata)
            .ToArray();
    }

    private sealed record ChapterMetadataRow(
        PlaybackChapterMetadata Metadata,
        int SortOrder);
}
