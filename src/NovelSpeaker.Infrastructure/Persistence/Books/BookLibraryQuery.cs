using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>
/// Reads detached library summary projections from SQLite.
/// </summary>
public sealed class BookLibraryQuery : IBookLibraryQuery
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public BookLibraryQuery(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken) =>
        QueryBooksAsync(bookIds: null, cancellationToken);

    public Task<IReadOnlyList<BookSummary>> GetBooksAsync(
        IReadOnlyCollection<string> bookIds,
        CancellationToken cancellationToken) =>
        QueryBooksAsync(bookIds, cancellationToken);

    private async Task<IReadOnlyList<BookSummary>> QueryBooksAsync(
        IReadOnlyCollection<string>? bookIds,
        CancellationToken cancellationToken)
    {
        var requestedBookIds = bookIds?
            .Where(static bookId => !string.IsNullOrWhiteSpace(bookId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedBookIds is { Length: 0 })
        {
            return [];
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var books = new List<BookSummary>();
        const int batchSize = 400;
        var batchCount = requestedBookIds is null
            ? 1
            : (requestedBookIds.Length + batchSize - 1) / batchSize;
        for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchBookIds = requestedBookIds is null
                ? null
                : requestedBookIds
                    .Skip(batchIndex * batchSize)
                    .Take(batchSize)
                    .ToArray();
            using var command = connection.CreateCommand();
            var bookFilter = batchBookIds is null
                ? string.Empty
                : $"WHERE b.Id IN ({string.Join(", ", batchBookIds.Select((_, index) => $"$bookId{index}"))})";
            command.CommandText =
                $"""
            SELECT b.Id,
                   b.Title,
                   b.Author,
                   COALESCE(
                       (SELECT c.Title
                        FROM ReadingProgress progress
                        INNER JOIN Chapters c
                            ON c.SourceId = b.ActiveSourceId
                           AND c.ChapterIndex = MAX(0, MIN(progress.ChapterIndex, chapterCounts.TotalChapterCount - 1))
                        WHERE progress.BookId = b.Id
                        ORDER BY progress.UpdatedAt DESC
                        LIMIT 1),
                       (SELECT c.Title
                        FROM Chapters c
                        WHERE c.SourceId = b.ActiveSourceId
                        ORDER BY c.SortOrder, c.ChapterIndex
                        LIMIT 1),
                       CASE WHEN b.ActiveSourceId IS NULL THEN '无活动来源' ELSE '未开始' END) AS CurrentChapterTitle,
                   b.ImportedAt,
                   b.LastPlayedAt,
                   COALESCE(chapterCounts.TotalChapterCount, 0) AS TotalChapterCount,
                   rp.ChapterIndex,
                   CASE WHEN rp.BookId IS NULL THEN 0 ELSE 1 END AS HasReadingProgress,
                   b.ActiveSourceId,
                   (SELECT Id FROM Chapters WHERE SourceId = b.ActiveSourceId ORDER BY ChapterIndex LIMIT 1)
            FROM Books b
            LEFT JOIN (
                SELECT SourceId, COUNT(*) AS TotalChapterCount
                FROM Chapters
                GROUP BY SourceId
            ) chapterCounts ON chapterCounts.SourceId = b.ActiveSourceId
            LEFT JOIN ReadingProgress rp ON rp.BookId = b.Id
            {bookFilter}
            ORDER BY b.ImportedAt DESC, b.Id;
            """;
            if (batchBookIds is not null)
            {
                for (var index = 0; index < batchBookIds.Length; index++)
                {
                    command.Parameters.AddWithValue($"$bookId{index}", batchBookIds[index]);
                }
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (TryMapSummary(reader, out var summary))
                {
                    books.Add(summary);
                }
            }
        }

        return books;
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
            var hasReadingProgress = reader.GetInt64(8) == 1 && currentChapterIndex is not null && totalChapterCount > 0;
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
                hasReadingProgress,
                reader.IsDBNull(9) ? null : new ActiveSourceContext(reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10)));
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
