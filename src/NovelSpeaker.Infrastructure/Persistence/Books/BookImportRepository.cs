using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>Commits a local binding, optional CurrentCatalog, content pointer and journal phase together.</summary>
public sealed class BookImportRepository(ISqliteConnectionFactory connectionFactory) : IBookImportRepository
{
    public async Task<LocalSourceImportTarget?> FindByIdentityAsync(
        BookIdentity identity, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Books WHERE NormalizedTitle = $title AND NormalizedAuthor = $author;";
        command.Parameters.AddWithValue("$title", identity.NormalizedTitle);
        command.Parameters.AddWithValue("$author", identity.NormalizedAuthor);
        var id = await command.ExecuteScalarAsync(cancellationToken) as string;
        return id is null ? null : await GetTargetAsync(id, cancellationToken);
    }

    public async Task<LocalSourceImportTarget?> GetTargetAsync(string bookId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.Id, b.Title, b.Author, b.ActiveSourceBindingId, b.ImportedAt, b.LastPlayedAt, b.UpdatedAt, b.Description,
                   s.Id, s.CreatedAt, s.UpdatedAt,
                   l.OriginalFileName, l.StoredContentPath, l.SourceHash, l.Encoding, l.ImportedAt, l.LastImportedAt
            FROM Books b
            LEFT JOIN BookSourceBindings s ON s.BookId = b.Id AND s.SourceType = 1
            LEFT JOIN LocalBookSourceBindings l ON l.BindingId = s.Id
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

        var binding = new BookSourceBinding(reader.GetString(8), bookId, SourceType.Local,
            SqliteDateTimeMapper.Parse(reader.GetString(9)), SqliteDateTimeMapper.Parse(reader.GetString(10)));
        var local = new LocalBookSourceBinding(binding.BindingId, reader.GetString(11), reader.GetString(12), reader.GetString(13),
            reader.GetString(14), SqliteDateTimeMapper.Parse(reader.GetString(15)), SqliteDateTimeMapper.Parse(reader.GetString(16)));
        return new LocalSourceImportTarget(book, binding, local);
    }

    public async Task SaveAsync(LocalSourceImportSnapshot snapshot, string operationId, CancellationToken cancellationToken)
    {
        Validate(snapshot);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var book = snapshot.Book;
        var source = snapshot.Binding;
        var local = snapshot.LocalBinding;

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
                INSERT INTO Books (Id, Title, Author, NormalizedTitle, NormalizedAuthor, Description, ActiveSourceBindingId, ImportedAt, LastPlayedAt, UpdatedAt)
                VALUES ($id, $title, $author, $normalizedTitle, $normalizedAuthor, $description, $source, $imported, $played, $updated);
                """, ("$id", book.Id), ("$title", book.Title), ("$author", book.Author), ("$description", book.Description),
                ("$normalizedTitle", book.NormalizedTitle), ("$normalizedAuthor", book.NormalizedAuthor),
                ("$source", source.BindingId), ("$imported", SqliteDateTimeMapper.Format(book.ImportedAt)),
                ("$played", book.LastPlayedAt is { } played ? SqliteDateTimeMapper.Format(played) : null),
                ("$updated", SqliteDateTimeMapper.Format(source.UpdatedAt)));
        }
        else
        {
            using var expected = connection.CreateCommand();
            expected.Transaction = transaction;
            expected.CommandText = """
                SELECT s.Id, l.StoredContentPath, b.ActiveSourceBindingId, b.NormalizedTitle, b.NormalizedAuthor FROM Books b
                LEFT JOIN BookSourceBindings s ON s.BookId = b.Id AND s.SourceType = 1
                LEFT JOIN LocalBookSourceBindings l ON l.BindingId = s.Id WHERE b.Id = $id;
                """;
            expected.Parameters.AddWithValue("$id", book.Id);
            await using var reader = await expected.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || NullableString(reader, 1) != snapshot.ExpectedContentPath ||
                (!reader.IsDBNull(0) && reader.GetString(0) != source.BindingId) ||
                NullableString(reader, 2) != book.ActiveSourceBindingId ||
                reader.GetString(3) != book.NormalizedTitle || reader.GetString(4) != book.NormalizedAuthor)
            {
                throw new InvalidOperationException("The local source changed while its snapshot was being prepared.");
            }
        }

        var sourceInsert = """
            INSERT INTO BookSourceBindings (Id, BookId, SourceType, CreatedAt, UpdatedAt)
            VALUES ($id, $book, 1, $created, $updated)
            """;
        if (snapshot.ExpectedContentPath is not null)
        {
            sourceInsert += " ON CONFLICT(Id) DO UPDATE SET UpdatedAt = excluded.UpdatedAt";
        }

        await ExecuteAsync(sourceInsert, ("$id", source.BindingId), ("$book", book.Id),
            ("$created", SqliteDateTimeMapper.Format(source.CreatedAt)),
            ("$updated", SqliteDateTimeMapper.Format(source.UpdatedAt)));
        await ExecuteAsync("""
            INSERT INTO LocalBookSourceBindings (BindingId, OriginalFileName, StoredContentPath, SourceHash, Encoding, ImportedAt, LastImportedAt)
            VALUES ($id, $file, $path, $hash, $encoding, $imported, $lastImported)
            ON CONFLICT(BindingId) DO UPDATE SET OriginalFileName = excluded.OriginalFileName,
                StoredContentPath = excluded.StoredContentPath, SourceHash = excluded.SourceHash,
                Encoding = excluded.Encoding, LastImportedAt = excluded.LastImportedAt;
            """, ("$id", source.BindingId), ("$file", local.OriginalFileName), ("$path", local.StoredContentPath),
            ("$hash", local.SourceHash), ("$encoding", local.Encoding), ("$imported", SqliteDateTimeMapper.Format(local.ImportedAt)),
            ("$lastImported", SqliteDateTimeMapper.Format(local.LastImportedAt)));
        if (snapshot.CurrentCatalog is { } catalog)
        {
            await ExecuteAsync("DELETE FROM Chapters WHERE BookId = $book;", ("$book", book.Id));
            foreach (var chapter in catalog.Entries)
            {
                await ExecuteAsync("""
                    INSERT INTO Chapters (Id, BookId, SourceBindingId, ChapterIndex, SortOrder, Title)
                    VALUES ($id, $book, $source, $index, $order, $title);
                    """, ("$id", chapter.Id), ("$book", book.Id), ("$source", source.BindingId), ("$index", chapter.ChapterIndex),
                    ("$order", chapter.SortOrder), ("$title", chapter.Title));
            }

            foreach (var content in snapshot.Contents)
            {
                await ExecuteAsync("INSERT INTO LocalChapterContents (ChapterId, StartOffset, Length) VALUES ($id, $start, $length);",
                    ("$id", content.ChapterId), ("$start", content.StartOffset), ("$length", content.Length));
            }
        }

        await ExecuteAsync("""
            UPDATE Books SET Description = $description, UpdatedAt = $updated
            WHERE Id = $id AND ActiveSourceBindingId = $source;
            """, ("$id", book.Id), ("$source", source.BindingId),
            ("$description", book.Description), ("$updated", SqliteDateTimeMapper.Format(source.UpdatedAt)));

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
        var catalog = snapshot.CurrentCatalog;
        var active = snapshot.Book.ActiveSourceBindingId == snapshot.Binding.BindingId;
        if (snapshot.Binding.BookId != snapshot.Book.Id || snapshot.Binding.SourceType != SourceType.Local ||
            snapshot.LocalBinding.BindingId != snapshot.Binding.BindingId ||
            (snapshot.IsNewBook && (!active || snapshot.ExpectedContentPath is not null)) ||
            active != (catalog is not null) ||
            (catalog is null && snapshot.Contents.Count != 0) ||
            (catalog is not null && (catalog.BookId != snapshot.Book.Id || catalog.SourceBindingId != snapshot.Binding.BindingId ||
                catalog.Entries.Count != snapshot.Contents.Count ||
                !catalog.Entries.Select(chapter => chapter.Id).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(snapshot.Contents.Select(content => content.ChapterId)))))
        {
            throw new InvalidDataException("The local binding snapshot is incomplete or has inconsistent ownership.");
        }
    }

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
