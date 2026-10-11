using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.TestKit.Common;
using Xunit;
using static NovelSpeaker.Infrastructure.IntegrationTests.Persistence.BookSourceMigrationTests;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Persistence;

public sealed class CurrentCatalogMigrationTests
{
    private static readonly string[] PreservedTables =
    [
        "ReadingProgress", "ChapterSpeechPlans", "ChapterSpeechPlanSegments",
        "SynthesisProfiles", "AudioCacheEntries", "BookOperations", "LocalChapterContents"
    ];

    [Fact]
    public async Task Version_12_upgrade_preserves_formal_data_files_and_current_reads_across_restarts()
    {
        using var directory = new TemporaryDirectory();
        var factory = await CreateVersion12FixtureAsync(directory.Path);
        var path = Path.Combine(directory.Path, "Books", "book-a", "content.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = new string('前', 12) + new string('文', 34);
        await File.WriteAllTextAsync(path, text);
        var snapshots = new Dictionary<string, string[]>();
        await using (var before = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            await ExecuteAsync(before, "UPDATE Books SET Title = '  Café　 Fixture ', Author = NULL WHERE Id = 'book-a';");
            foreach (var table in PreservedTables)
                snapshots[table] = await SnapshotAsync(before, $"SELECT * FROM {table} ORDER BY 1, 2;");
            snapshots["Books"] = await SnapshotAsync(before,
                "SELECT Id, Title, Author, Description, ActiveSourceId, ImportedAt, LastPlayedAt, UpdatedAt FROM Books ORDER BY Id;");
            snapshots["Bindings"] = await SnapshotAsync(before,
                "SELECT Id, BookId, SourceType, CreatedAt, UpdatedAt FROM BookSources ORDER BY Id;");
            snapshots["Local"] = await SnapshotAsync(before, "SELECT * FROM LocalBookSources ORDER BY SourceId;");
            snapshots["Chapters"] = await SnapshotAsync(before,
                "SELECT c.Id, s.BookId, c.SourceId, c.ChapterIndex, c.SortOrder, c.Title FROM Chapters c JOIN BookSources s ON s.Id = c.SourceId ORDER BY c.Id;");
        }

        await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
        var restarted = await CreateFactoryAsync(directory.Path);
        await new SqliteMigrationRunner(restarted).InitializeAsync(CancellationToken.None);
        await using (var after = await restarted.OpenConnectionAsync(CancellationToken.None))
        {
            Assert.Equal(13L, await ScalarAsync(after, "SELECT MAX(Version) FROM SchemaVersion;"));
            foreach (var table in PreservedTables)
                Assert.Equal(snapshots[table], await SnapshotAsync(after, $"SELECT * FROM {table} ORDER BY 1, 2;"));
            Assert.Equal(snapshots["Books"], await SnapshotAsync(after,
                "SELECT Id, Title, Author, Description, ActiveSourceBindingId, ImportedAt, LastPlayedAt, UpdatedAt FROM Books ORDER BY Id;"));
            Assert.Equal(snapshots["Bindings"], await SnapshotAsync(after,
                "SELECT Id, BookId, SourceType, CreatedAt, UpdatedAt FROM BookSourceBindings ORDER BY Id;"));
            Assert.Equal(snapshots["Local"], await SnapshotAsync(after, "SELECT * FROM LocalBookSourceBindings ORDER BY BindingId;"));
            Assert.Equal(snapshots["Chapters"], await SnapshotAsync(after,
                "SELECT Id, BookId, SourceBindingId, ChapterIndex, SortOrder, Title FROM Chapters ORDER BY Id;"));
            Assert.Equal(0L, await ScalarAsync(after,
                "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('BookSources', 'LocalBookSources') OR name LIKE '%_V13';"));
            await AssertIntegrityAsync(after);
            Assert.Equal("ok", await ScalarAsync(after, "PRAGMA integrity_check;"));
        }

        Assert.Equal(text, await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(directory.Path, "content.txt", SearchOption.AllDirectories));
        var repository = new BookImportRepository(restarted);
        var target = await repository.FindByIdentityAsync(BookIdentity.Create("Café\tFixture", ""), CancellationToken.None);
        Assert.Equal("book-a", target!.Book.Id);
        Assert.Equal("local:book-a", target.Binding!.BindingId);
        var query = new SqliteBookPlaybackMetadataQuery(restarted);
        var chapter = await query.GetChapterAsync("book-a", 0, CancellationToken.None);
        Assert.Equal("book-a", chapter!.BookId);
        Assert.Equal("chapter-a", chapter.ChapterId);
        var reader = new SourceContentReader(new AppStoragePathResolver(new AppDataDirectoryProvider(directory.Path)), restarted);
        Assert.Equal(new string('文', 34), await reader.ReadChapterTextAsync(chapter, CancellationToken.None));
        Assert.Equal(text, await reader.ReadBookTextAsync("book-a", chapter.SourceContext!, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadChapterTextAsync(
            chapter with { BookId = "book-b" }, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadBookTextAsync(
            "book-a", chapter.SourceContext! with { CatalogVersion = "obsolete" }, CancellationToken.None));
        Assert.Equal(3, (await new BookLibraryQuery(restarted).GetBooksAsync(CancellationToken.None)).Count);
        Assert.Equal("local:book-a", (await new BookDetailsQuery(restarted).GetHeaderAsync("book-a", CancellationToken.None))!.ActiveSource!.Context.SourceId);
        Assert.Equal("chapter-a", Assert.Single(await new BookDetailsQuery(restarted).GetCatalogAsync("book-a", CancellationToken.None)).ChapterId);
        Assert.Equal(2, (await new SqliteReadingProgressStore(restarted).GetAsync("book-a", CancellationToken.None))!.SegmentIndex);
    }

    [Theory]
    [InlineData("UPDATE Books SET Title = '  Fixture　 A ', Author = ' Author　A ' WHERE Id = 'book-b';")]
    [InlineData("UPDATE Books SET Title = 'Café', Author = NULL WHERE Id = 'book-a'; UPDATE Books SET Title = ' Café ', Author = '　' WHERE Id = 'book-b';")]
    [InlineData("UPDATE Books SET ActiveSourceId = NULL WHERE Id = 'book-a';")]
    [InlineData("DELETE FROM LocalBookSources WHERE SourceId = 'local:book-a';")]
    [InlineData("UPDATE Chapters SET ChapterIndex = 2 WHERE Id = 'chapter-a';")]
    public async Task Incompatible_identity_or_catalog_rolls_back_entire_library(string invalidState)
    {
        using var directory = new TemporaryDirectory();
        var factory = await CreateVersion12FixtureAsync(directory.Path);
        var path = Path.Combine(directory.Path, "Books", "book-a", "content.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "sanitized persistent local content");
        await using (var before = await factory.OpenConnectionAsync(CancellationToken.None))
            await ExecuteAsync(before, invalidState);
        var snapshot = await SnapshotLibraryAsync(factory);
        await Assert.ThrowsAsync<IncompatibleBookLibraryException>(() => new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None));
        Assert.Equal(snapshot, await SnapshotLibraryAsync(factory));
        Assert.Equal("sanitized persistent local content", await File.ReadAllTextAsync(path));
        await using var after = await factory.OpenConnectionAsync(CancellationToken.None);
        Assert.Equal(12L, await ScalarAsync(after, "SELECT MAX(Version) FROM SchemaVersion;"));
        Assert.Equal(0L, await ScalarAsync(after, "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE '%_V13' OR name = 'BookSourceBindings';"));
        await AssertIntegrityAsync(after);
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("callback")]
    [InlineData("cancel")]
    public async Task Failed_rebuild_rolls_back_schema_data_and_version_then_can_retry(string failure)
    {
        using var directory = new TemporaryDirectory();
        var factory = await CreateVersion12FixtureAsync(directory.Path);
        var before = await SnapshotLibraryAsync(factory);
        using var cancellation = new CancellationTokenSource();
        var migration = SqliteMigrationRunner.AllMigrations.Single(m => m.Version == 13);
        var apply = migration.ApplyDataAsync!;
        migration = failure == "sql"
            ? migration with { Sql = migration.Sql + "\nINSERT INTO MissingMigrationTable VALUES (1);" }
            : migration with
            {
                ApplyDataAsync = async (connection, transaction, token) =>
                {
                    await apply(connection, transaction, token);
                    if (failure == "cancel")
                    {
                        cancellation.Cancel();
                        cancellation.Token.ThrowIfCancellationRequested();
                    }
                    throw new InvalidOperationException("Simulated migration failure.");
                }
            };
        var runner = new SqliteMigrationRunner(factory, [migration]);
        if (failure == "sql")
            await Assert.ThrowsAsync<SqliteException>(() => runner.InitializeAsync(cancellation.Token));
        else if (failure == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.InitializeAsync(cancellation.Token));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => runner.InitializeAsync(cancellation.Token));
        Assert.Equal(before, await SnapshotLibraryAsync(factory));
        await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
        await using var after = await factory.OpenConnectionAsync(CancellationToken.None);
        Assert.Equal(13L, await ScalarAsync(after, "SELECT MAX(Version) FROM SchemaVersion;"));
        await AssertIntegrityAsync(after);
    }

    [Fact]
    public async Task Current_catalog_constraints_reject_wrong_ownership_and_delete_last_binding_cascades()
    {
        using var directory = new TemporaryDirectory();
        var factory = await CreateVersion12FixtureAsync(directory.Path);
        await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        string[] invalidWrites =
        [
            "UPDATE Books SET ActiveSourceBindingId = 'local:book-b' WHERE Id = 'book-a';",
            "UPDATE Books SET ActiveSourceBindingId = NULL WHERE Id = 'book-a';",
            "INSERT INTO BookSourceBindings (Id, BookId, SourceType, CreatedAt, UpdatedAt) VALUES ('extra', 'book-a', 1, 'now', 'now');",
            "INSERT INTO Chapters (Id, BookId, SourceBindingId, ChapterIndex, Title) VALUES ('wrong-book', 'book-b', 'local:book-a', 0, 'fixture');",
            "INSERT INTO Chapters (Id, BookId, SourceBindingId, ChapterIndex, Title) VALUES ('duplicate', 'book-a', 'local:book-a', 0, 'fixture');",
            "UPDATE Books SET NormalizedTitle = 'Fixture A', NormalizedAuthor = 'Author A' WHERE Id = 'book-b';",
            "UPDATE LocalChapterContents SET StartOffset = -1 WHERE ChapterId = 'chapter-a';"
        ];
        foreach (var sql in invalidWrites)
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, sql));
        await ExecuteAsync(connection, "DELETE FROM BookSourceBindings WHERE Id = 'local:book-a';");
        foreach (var table in new[] { "Chapters", "LocalChapterContents", "ChapterSpeechPlans", "ChapterSpeechPlanSegments", "AudioCacheEntries", "ReadingProgress" })
            Assert.Equal(0L, await ScalarAsync(connection, $"SELECT COUNT(*) FROM {table};"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Books WHERE Id = 'book-a';"));
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Books;"));
        await ExecuteAsync(connection, "DELETE FROM Books;");
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM BookSourceBindings;"));
        await AssertIntegrityAsync(connection);
    }

    [Fact]
    public async Task Fresh_library_commits_local_snapshot_atomically_and_rejects_stale_or_unjournaled_updates()
    {
        using var directory = new TemporaryDirectory();
        var factory = await CreateFactoryAsync(directory.Path);
        await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
        var repository = new BookImportRepository(factory);
        var now = DateTimeOffset.UtcNow;
        var book = new Book("book", "Fixture", null, "binding", now, null, now);
        var binding = new BookSourceBinding("binding", book.Id, SourceType.Local, now, now);
        var local = new LocalBookSourceBinding("binding", "fixture.txt", "Books/book/content.txt", "hash", "utf-8", now, now);
        var catalog = new CurrentCatalog(book.Id, binding.BindingId, [new("chapter", book.Id, binding.BindingId, 0, 0, "fixture")]);
        var snapshot = new LocalSourceImportSnapshot(book, binding, local, catalog, [new("chapter", 0, 10)], true, null);
        await StageImportAsync(factory, book.Id);
        await repository.SaveAsync(snapshot, "import", CancellationToken.None);
        var before = await SnapshotLibraryAsync(factory);
        var replacement = snapshot with
        {
            IsNewBook = false,
            ExpectedContentPath = local.StoredContentPath,
            LocalBinding = local with { StoredContentPath = "Books/book/content-new.txt" },
            CurrentCatalog = new(book.Id, binding.BindingId, [new("replacement", book.Id, binding.BindingId, 0, 0, "replacement")]),
            Contents = [new("replacement", 0, 20)]
        };
        // Failure after replacing rows must restore the original pointer, catalog and journal.
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(replacement, "missing", CancellationToken.None));
        Assert.Equal(before, await SnapshotLibraryAsync(factory));
        await StageImportAsync(factory, book.Id);
        await repository.SaveAsync(replacement, "import", CancellationToken.None);
        Assert.Equal(replacement.LocalBinding.StoredContentPath, (await repository.GetTargetAsync(book.Id, CancellationToken.None))!.LocalBinding!.StoredContentPath);
        Assert.Equal("replacement", Assert.Single((await new SqliteBookPlaybackMetadataQuery(factory).GetBookAsync(book.Id, CancellationToken.None))!.Chapters).ChapterId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(replacement, "import", CancellationToken.None));
        await using var after = await factory.OpenConnectionAsync(CancellationToken.None);
        await AssertIntegrityAsync(after);

        // An inactive local binding retains only content, never a hidden second catalog.
        await ExecuteAsync(after, "DELETE FROM Chapters; UPDATE Books SET ActiveSourceBindingId = NULL WHERE Id = 'book';");
        await StageImportAsync(factory, book.Id);
        var inactive = replacement with
        {
            Book = book with { ActiveSourceBindingId = null },
            ExpectedContentPath = replacement.LocalBinding.StoredContentPath,
            LocalBinding = local with { StoredContentPath = "Books/book/content-inactive.txt" },
            CurrentCatalog = null,
            Contents = []
        };
        await repository.SaveAsync(inactive, "import", CancellationToken.None);
        Assert.Equal(inactive.LocalBinding.StoredContentPath, (await repository.GetTargetAsync(book.Id, CancellationToken.None))!.LocalBinding!.StoredContentPath);
        Assert.Equal(0L, await ScalarAsync(after, "SELECT COUNT(*) FROM Chapters;"));
        Assert.Null((await new SqliteBookPlaybackMetadataQuery(factory).GetBookAsync(book.Id, CancellationToken.None))!.SourceContext);
        await AssertIntegrityAsync(after);
    }

    private static async Task StageImportAsync(ISqliteConnectionFactory factory, string bookId)
    {
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO BookOperations (OperationId, Kind, Phase, BookId, PathsJson, CreatedAt, UpdatedAt)
            VALUES ('import', 'Import', 'Staged', $book, '[]', '2026-01-01', '2026-01-01')
            ON CONFLICT(OperationId) DO UPDATE SET Phase = 'Staged';
            """;
        command.Parameters.AddWithValue("$book", bookId);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<SqliteConnectionFactory> CreateVersion12FixtureAsync(string root)
    {
        var factory = await CreateVersion11FixtureAsync(root);
        await new SqliteMigrationRunner(factory, SqliteMigrationRunner.AllMigrations.Where(m => m.Version == 12).ToArray())
            .InitializeAsync(CancellationToken.None);
        return factory;
    }

    private static async Task<string[]> SnapshotLibraryAsync(ISqliteConnectionFactory factory)
    {
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var rows = new List<string>(await SnapshotAsync(connection, "SELECT type, name, sql FROM sqlite_master ORDER BY name;"));
        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
            while (await reader.ReadAsync(CancellationToken.None)) tables.Add(reader.GetString(0));
        }
        foreach (var table in tables)
        {
            rows.Add(table);
            rows.AddRange(await SnapshotAsync(connection, $"SELECT * FROM {table} ORDER BY 1;"));
        }
        return rows.ToArray();
    }
}
