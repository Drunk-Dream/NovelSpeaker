using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Saves imported books and chapters inside a single SQLite transaction.
/// </summary>
public sealed class BookImportRepository : IBookImportRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public BookImportRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var bookCommand = connection.CreateCommand();
            bookCommand.Transaction = transaction;
            bookCommand.CommandText =
                """
                INSERT INTO Books (Id, Title, Author, OriginalFileName, StoredFilePath, SourceHash, Encoding, ImportedAt, LastImportedAt, LastPlayedAt, UpdatedAt)
                VALUES ($id, $title, $author, $originalFileName, $storedFilePath, $sourceHash, $encoding, $importedAt, $lastImportedAt, $lastPlayedAt, $updatedAt);
                """;
            bookCommand.Parameters.AddWithValue("$id", book.Id);
            bookCommand.Parameters.AddWithValue("$title", book.Title);
            bookCommand.Parameters.AddWithValue("$author", (object?)book.Author ?? DBNull.Value);
            bookCommand.Parameters.AddWithValue("$originalFileName", book.OriginalFileName);
            bookCommand.Parameters.AddWithValue("$storedFilePath", book.StoredFilePath);
            bookCommand.Parameters.AddWithValue("$sourceHash", book.SourceHash);
            bookCommand.Parameters.AddWithValue("$encoding", book.Encoding);
            bookCommand.Parameters.AddWithValue("$importedAt", book.ImportedAt);
            bookCommand.Parameters.AddWithValue("$lastImportedAt", book.LastImportedAt);
            bookCommand.Parameters.AddWithValue("$lastPlayedAt", (object?)book.LastPlayedAt ?? DBNull.Value);
            bookCommand.Parameters.AddWithValue("$updatedAt", book.UpdatedAt);
            await bookCommand.ExecuteNonQueryAsync(cancellationToken);

            foreach (var chapter in chapters)
            {
                var chapterCommand = connection.CreateCommand();
                chapterCommand.Transaction = transaction;
                chapterCommand.CommandText =
                    """
                    INSERT INTO Chapters (Id, BookId, ChapterIndex, SortOrder, Title, Content, StartOffset, Length)
                    VALUES ($id, $bookId, $chapterIndex, $sortOrder, $title, $content, $startOffset, $length);
                    """;
                chapterCommand.Parameters.AddWithValue("$id", chapter.Id);
                chapterCommand.Parameters.AddWithValue("$bookId", chapter.BookId);
                chapterCommand.Parameters.AddWithValue("$chapterIndex", chapter.ChapterIndex);
                chapterCommand.Parameters.AddWithValue("$sortOrder", chapter.SortOrder);
                chapterCommand.Parameters.AddWithValue("$title", chapter.Title);
                chapterCommand.Parameters.AddWithValue("$content", chapter.Content);
                chapterCommand.Parameters.AddWithValue("$startOffset", chapter.StartOffset);
                chapterCommand.Parameters.AddWithValue("$length", chapter.Length);
                await chapterCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
