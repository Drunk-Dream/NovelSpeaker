using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>Commits source metadata, catalog, content pointer and journal phase together.</summary>
public sealed class BookImportRepository(ISqliteConnectionFactory connectionFactory) : IBookImportRepository
{
    public async Task<IReadOnlyList<BookImportCandidate>> FindCandidatesAsync(
        string title, string? author, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.Id, b.Title, b.Author, b.ImportedAt, b.LastPlayedAt, l.OriginalFileName FROM Books b
            LEFT JOIN BookSources s ON s.BookId = b.Id AND s.SourceType = 1
            LEFT JOIN LocalBookSources l ON l.SourceId = s.Id
            WHERE b.Title = $title COLLATE BINARY AND COALESCE(b.Author, '') = $author COLLATE BINARY;
            """;
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$author", author ?? string.Empty);
        var candidates = new List<BookImportCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new BookImportCandidate(reader.GetString(0), reader.GetString(1), NullableString(reader, 2),
                SqliteDateTimeMapper.Parse(reader.GetString(3)),
                reader.IsDBNull(4) ? null : SqliteDateTimeMapper.Parse(reader.GetString(4)), NullableString(reader, 5)));
        }

        return candidates;
    }

    public async Task<LocalSourceImportTarget?> GetTargetAsync(string bookId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.Id, b.Title, b.Author, b.ActiveSourceId, b.ImportedAt, b.LastPlayedAt, b.UpdatedAt, b.Description,
                   s.Id, s.Title, s.Author, s.Description, s.CreatedAt, s.UpdatedAt,
                   l.OriginalFileName, l.StoredContentPath, l.SourceHash, l.Encoding, l.ImportedAt, l.LastImportedAt
            FROM Books b
            LEFT JOIN BookSources s ON s.BookId = b.Id AND s.SourceType = 1
            LEFT JOIN LocalBookSources l ON l.SourceId = s.Id
            WHERE b.Id = $bookId;
            """;
        command.Parameters.AddWithValue("$bookId", bookId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var book = new Book(reader.GetString(0), reader.GetString(1), NullableString(reader, 2), NullableString(reader, 3),
            SqliteDateTimeMapper.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : SqliteDateTimeMapper.Parse(reader.GetString(5)),
            SqliteDateTimeMapper.Parse(reader.GetString(6)), NullableString(reader, 7));
        if (reader.IsDBNull(8))
        {
            return new LocalSourceImportTarget(book, null, null);
        }

        var source = new BookSource(reader.GetString(8), bookId, SourceType.Local, reader.GetString(9),
            NullableString(reader, 10), NullableString(reader, 11), SqliteDateTimeMapper.Parse(reader.GetString(12)),
            SqliteDateTimeMapper.Parse(reader.GetString(13)));
        var local = new LocalBookSource(source.Id, reader.GetString(14), reader.GetString(15), reader.GetString(16),
            reader.GetString(17), SqliteDateTimeMapper.Parse(reader.GetString(18)), SqliteDateTimeMapper.Parse(reader.GetString(19)));
        return new LocalSourceImportTarget(book, source, local);
    }

    public async Task SaveAsync(LocalSourceImportSnapshot snapshot, string operationId, CancellationToken cancellationToken)
    {
        Validate(snapshot);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var book = snapshot.Book;
        var source = snapshot.Source;
        var local = snapshot.LocalSource;

        async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (snapshot.IsNewBook)
        {
            await ExecuteAsync("""
                INSERT INTO Books (Id, Title, Author, Description, ActiveSourceId, ImportedAt, LastPlayedAt, UpdatedAt)
                VALUES ($id, $title, $author, $description, $source, $imported, $played, $updated);
                """, ("$id", book.Id), ("$title", source.Title), ("$author", source.Author), ("$description", source.Description),
                ("$source", source.Id), ("$imported", SqliteDateTimeMapper.Format(book.ImportedAt)),
                ("$played", book.LastPlayedAt is { } played ? SqliteDateTimeMapper.Format(played) : null),
                ("$updated", SqliteDateTimeMapper.Format(source.UpdatedAt)));
        }
        else
        {
            using var expected = connection.CreateCommand();
            expected.Transaction = transaction;
            expected.CommandText = """
                SELECT s.Id, l.StoredContentPath FROM Books b
                LEFT JOIN BookSources s ON s.BookId = b.Id AND s.SourceType = 1
                LEFT JOIN LocalBookSources l ON l.SourceId = s.Id WHERE b.Id = $id;
                """;
            expected.Parameters.AddWithValue("$id", book.Id);
            await using var reader = await expected.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || NullableString(reader, 1) != snapshot.ExpectedContentPath ||
                (!reader.IsDBNull(0) && reader.GetString(0) != source.Id))
            {
                throw new InvalidOperationException("The local source changed while its snapshot was being prepared.");
            }
        }

        var sourceInsert = """
            INSERT INTO BookSources (Id, BookId, SourceType, Title, Author, Description, CreatedAt, UpdatedAt)
            VALUES ($id, $book, 1, $title, $author, $description, $created, $updated)
            """;
        if (snapshot.ExpectedContentPath is not null)
        {
            sourceInsert += " ON CONFLICT(Id) DO UPDATE SET Title = excluded.Title, Author = excluded.Author, Description = excluded.Description, UpdatedAt = excluded.UpdatedAt";
        }

        await ExecuteAsync(sourceInsert, ("$id", source.Id), ("$book", book.Id), ("$title", source.Title), ("$author", source.Author),
            ("$description", source.Description), ("$created", SqliteDateTimeMapper.Format(source.CreatedAt)),
            ("$updated", SqliteDateTimeMapper.Format(source.UpdatedAt)));
        await ExecuteAsync("""
            INSERT INTO LocalBookSources (SourceId, OriginalFileName, StoredContentPath, SourceHash, Encoding, ImportedAt, LastImportedAt)
            VALUES ($id, $file, $path, $hash, $encoding, $imported, $lastImported)
            ON CONFLICT(SourceId) DO UPDATE SET OriginalFileName = excluded.OriginalFileName,
                StoredContentPath = excluded.StoredContentPath, SourceHash = excluded.SourceHash,
                Encoding = excluded.Encoding, LastImportedAt = excluded.LastImportedAt;
            """, ("$id", source.Id), ("$file", local.OriginalFileName), ("$path", local.StoredContentPath),
            ("$hash", local.SourceHash), ("$encoding", local.Encoding), ("$imported", SqliteDateTimeMapper.Format(local.ImportedAt)),
            ("$lastImported", SqliteDateTimeMapper.Format(local.LastImportedAt)));
        await ExecuteAsync("DELETE FROM Chapters WHERE SourceId = $source;", ("$source", source.Id));
        foreach (var chapter in snapshot.Catalog)
        {
            await ExecuteAsync("""
                INSERT INTO Chapters (Id, SourceId, ChapterIndex, SortOrder, Title)
                VALUES ($id, $source, $index, $order, $title);
                """, ("$id", chapter.Id), ("$source", source.Id), ("$index", chapter.ChapterIndex),
                ("$order", chapter.SortOrder), ("$title", chapter.Title));
        }

        foreach (var content in snapshot.Contents)
        {
            await ExecuteAsync("INSERT INTO LocalChapterContents (ChapterId, StartOffset, Length) VALUES ($id, $start, $length);",
                ("$id", content.ChapterId), ("$start", content.StartOffset), ("$length", content.Length));
        }

        await ExecuteAsync("""
            UPDATE Books SET Title = $title, Author = $author, Description = $description, UpdatedAt = $updated
            WHERE Id = $id AND ActiveSourceId = $source;
            """, ("$id", book.Id), ("$source", source.Id), ("$title", source.Title), ("$author", source.Author),
            ("$description", source.Description), ("$updated", SqliteDateTimeMapper.Format(source.UpdatedAt)));

        using var journal = connection.CreateCommand();
        journal.Transaction = transaction;
        journal.CommandText = """
            UPDATE BookOperations SET Phase = 'DatabaseCommitted', UpdatedAt = $updated
            WHERE OperationId = $operation AND BookId = $book AND Kind = 'Import' AND Phase = 'Staged';
            """;
        journal.Parameters.AddWithValue("$updated", SqliteDateTimeMapper.Format(source.UpdatedAt));
        journal.Parameters.AddWithValue("$operation", operationId);
        journal.Parameters.AddWithValue("$book", book.Id);
        if (await journal.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The staged import operation is missing.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static void Validate(LocalSourceImportSnapshot snapshot)
    {
        if (snapshot.Source.BookId != snapshot.Book.Id || snapshot.Source.Type != SourceType.Local ||
            snapshot.LocalSource.SourceId != snapshot.Source.Id || snapshot.Catalog.Count == 0 ||
            snapshot.Catalog.Count != snapshot.Contents.Count ||
            snapshot.Catalog.Any(chapter => chapter.SourceId != snapshot.Source.Id) ||
            !snapshot.Catalog.Select(chapter => chapter.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals(snapshot.Contents.Select(content => content.ChapterId)))
        {
            throw new InvalidDataException("The local source snapshot is incomplete or has inconsistent ownership.");
        }
    }

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
