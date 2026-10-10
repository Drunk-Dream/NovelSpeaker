using Microsoft.Data.Sqlite;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

internal static class CurrentCatalogMigration
{
    internal const string SchemaSql = """
        CREATE TABLE Books_V13 (
            Id TEXT NOT NULL PRIMARY KEY,
            Title TEXT NOT NULL,
            Author TEXT NULL,
            NormalizedTitle TEXT NOT NULL,
            NormalizedAuthor TEXT NOT NULL,
            Description TEXT NULL,
            ActiveSourceBindingId TEXT NULL,
            ImportedAt TEXT NOT NULL,
            LastPlayedAt TEXT NULL,
            UpdatedAt TEXT NOT NULL,
            UNIQUE(NormalizedTitle, NormalizedAuthor),
            UNIQUE(ActiveSourceBindingId, Id),
            FOREIGN KEY(ActiveSourceBindingId, Id) REFERENCES BookSourceBindings(Id, BookId)
                DEFERRABLE INITIALLY DEFERRED
        );

        CREATE TABLE BookSourceBindings (
            Id TEXT NOT NULL PRIMARY KEY,
            BookId TEXT NOT NULL,
            SourceType INTEGER NOT NULL CHECK(SourceType = 1),
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            FOREIGN KEY(BookId) REFERENCES Books(Id) ON DELETE CASCADE,
            UNIQUE(Id, BookId)
        );
        CREATE UNIQUE INDEX IX_BookSourceBindings_LocalSingleton
            ON BookSourceBindings(BookId) WHERE SourceType = 1;

        CREATE TABLE LocalBookSourceBindings (
            BindingId TEXT NOT NULL PRIMARY KEY,
            OriginalFileName TEXT NOT NULL,
            StoredContentPath TEXT NOT NULL,
            SourceHash TEXT NOT NULL,
            Encoding TEXT NOT NULL,
            ImportedAt TEXT NOT NULL,
            LastImportedAt TEXT NOT NULL,
            FOREIGN KEY(BindingId) REFERENCES BookSourceBindings(Id) ON DELETE CASCADE
        );

        CREATE TABLE Chapters_V13 (
            Id TEXT NOT NULL PRIMARY KEY,
            BookId TEXT NOT NULL,
            SourceBindingId TEXT NOT NULL,
            ChapterIndex INTEGER NOT NULL CHECK(ChapterIndex >= 0),
            SortOrder INTEGER NOT NULL DEFAULT 0,
            Title TEXT NOT NULL,
            UNIQUE(BookId, ChapterIndex),
            FOREIGN KEY(BookId) REFERENCES Books(Id) ON DELETE CASCADE,
            FOREIGN KEY(SourceBindingId, BookId) REFERENCES BookSourceBindings(Id, BookId) ON DELETE CASCADE,
            FOREIGN KEY(SourceBindingId, BookId) REFERENCES Books(ActiveSourceBindingId, Id)
                DEFERRABLE INITIALLY DEFERRED
        );
        """;

    internal static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        // Every legacy chapter must unambiguously belong to the active local snapshot.
        // Also reject missing typed rows and non-contiguous ordinals rather than repairing data.
        using (var validate = connection.CreateCommand())
        {
            validate.Transaction = transaction;
            validate.CommandText = """
                SELECT EXISTS(SELECT 1 FROM pragma_foreign_key_check('Books'))
                    OR EXISTS(SELECT 1 FROM pragma_foreign_key_check('BookSources'))
                    OR EXISTS(SELECT 1 FROM pragma_foreign_key_check('LocalBookSources'))
                    OR EXISTS(SELECT 1 FROM pragma_foreign_key_check('Chapters'))
                    OR EXISTS(SELECT 1 FROM pragma_foreign_key_check('LocalChapterContents'))
                    OR EXISTS(SELECT 1 FROM BookSources s LEFT JOIN LocalBookSources l ON l.SourceId = s.Id
                              WHERE s.SourceType <> 1 OR l.SourceId IS NULL)
                    OR EXISTS(SELECT 1 FROM Chapters c
                              JOIN BookSources s ON s.Id = c.SourceId JOIN Books b ON b.Id = s.BookId
                              LEFT JOIN LocalChapterContents l ON l.ChapterId = c.Id
                              WHERE b.ActiveSourceId IS NULL OR b.ActiveSourceId <> s.Id OR l.ChapterId IS NULL)
                    OR EXISTS(SELECT 1 FROM Chapters GROUP BY SourceId
                              HAVING MIN(ChapterIndex) <> 0 OR MAX(ChapterIndex) <> COUNT(*) - 1);
                """;
            if (Convert.ToInt32(await validate.ExecuteScalarAsync(cancellationToken)) != 0)
                throw new IncompatibleBookLibraryException();
        }

        var identities = new HashSet<BookIdentity>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Id, Title, Author FROM Books ORDER BY Id;";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                BookIdentity identity;
                try
                {
                    identity = BookIdentity.Create(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
                }
                catch (ArgumentException)
                {
                    throw new IncompatibleBookLibraryException();
                }

                if (!identities.Add(identity))
                    throw new IncompatibleBookLibraryException();

                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO Books_V13
                        (Id, Title, Author, NormalizedTitle, NormalizedAuthor, Description,
                         ActiveSourceBindingId, ImportedAt, LastPlayedAt, UpdatedAt)
                    SELECT Id, Title, Author, $title, $author, Description, ActiveSourceId,
                           ImportedAt, LastPlayedAt, UpdatedAt FROM Books WHERE Id = $id;
                    """;
                insert.Parameters.AddWithValue("$id", reader.GetString(0));
                insert.Parameters.AddWithValue("$title", identity.NormalizedTitle);
                insert.Parameters.AddWithValue("$author", identity.NormalizedAuthor);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        using var rebuild = connection.CreateCommand();
        rebuild.Transaction = transaction;
        rebuild.CommandText = """
            INSERT INTO BookSourceBindings (Id, BookId, SourceType, CreatedAt, UpdatedAt)
            SELECT Id, BookId, SourceType, CreatedAt, UpdatedAt FROM BookSources;
            INSERT INTO LocalBookSourceBindings
            SELECT SourceId, OriginalFileName, StoredContentPath, SourceHash, Encoding, ImportedAt, LastImportedAt
            FROM LocalBookSources;
            INSERT INTO Chapters_V13 (Id, BookId, SourceBindingId, ChapterIndex, SortOrder, Title)
            SELECT c.Id, s.BookId, c.SourceId, c.ChapterIndex, c.SortOrder, c.Title
            FROM Chapters c JOIN BookSources s ON s.Id = c.SourceId;

            DROP TRIGGER BookSources_ClearActiveSource;
            DROP TABLE Chapters;
            DROP TABLE LocalBookSources;
            DROP TABLE BookSources;
            DROP TABLE Books;
            ALTER TABLE Books_V13 RENAME TO Books;
            ALTER TABLE Chapters_V13 RENAME TO Chapters;
            CREATE INDEX IX_Chapters_SourceBindingId ON Chapters(SourceBindingId);

            CREATE TRIGGER BookSourceBindings_ClearActive BEFORE DELETE ON BookSourceBindings
            BEGIN
                UPDATE Books SET ActiveSourceBindingId = NULL WHERE Id = OLD.BookId AND ActiveSourceBindingId = OLD.Id;
            END;
            CREATE TRIGGER BookSourceBindings_DeleteEmptyBook AFTER DELETE ON BookSourceBindings
            WHEN NOT EXISTS (SELECT 1 FROM BookSourceBindings WHERE BookId = OLD.BookId)
            BEGIN
                DELETE FROM Books WHERE Id = OLD.BookId;
            END;
            """;
        await rebuild.ExecuteNonQueryAsync(cancellationToken);
    }
}
