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

}
