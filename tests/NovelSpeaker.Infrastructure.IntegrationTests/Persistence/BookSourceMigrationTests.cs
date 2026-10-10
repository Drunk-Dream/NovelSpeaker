using System.Globalization;
using Microsoft.Data.Sqlite;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Persistence;

public sealed class BookSourceMigrationTests
{
    private static readonly string[] PreservedTables =
    [
        "ReadingProgress", "ChapterSpeechPlans", "ChapterSpeechPlanSegments",
        "SynthesisProfiles", "AudioCacheEntries", "BookOperations"
    ];

    [Fact]
    public async Task Fresh_database_has_source_owned_catalog_and_only_typed_local_content()
    {
        using var directory = new TemporaryDirectory();
        var factory = await CreateFactoryAsync(directory.Path);
        await CreateVersion12Runner(factory).InitializeAsync(CancellationToken.None);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);

        Assert.Equal(12L, await ScalarAsync(connection, "SELECT MAX(Version) FROM SchemaVersion;"));
        Assert.Equal(3L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('BookSources', 'LocalBookSources', 'LocalChapterContents');"));
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('Books') WHERE name IN ('OriginalFileName', 'StoredFilePath', 'SourceHash', 'Encoding', 'LastImportedAt');"));
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('Chapters') WHERE name IN ('BookId', 'StartOffset', 'Length');"));
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE name = 'IX_Books_SourceHash' OR name LIKE '%_V12';"));
        await AssertIntegrityAsync(connection);
    }

    [Fact]
    public async Task Book_and_active_local_source_can_be_created_in_one_atomic_snapshot()
    {
        using var directory = new TemporaryDirectory();
        var factory = await CreateFactoryAsync(directory.Path);
        await CreateVersion12Runner(factory).InitializeAsync(CancellationToken.None);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(CancellationToken.None))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO Books (Id, Title, ActiveSourceId, ImportedAt, UpdatedAt)
                VALUES ('book', 'Fixture', 'source', 'now', 'now');
                INSERT INTO BookSources (Id, BookId, SourceType, Title, CreatedAt, UpdatedAt)
                VALUES ('source', 'book', 1, 'Fixture', 'now', 'now');
                INSERT INTO LocalBookSources (SourceId, OriginalFileName, StoredContentPath, SourceHash, Encoding, ImportedAt, LastImportedAt)
                VALUES ('source', 'fixture.txt', 'Books/book/content.txt', 'hash', 'utf-8', 'now', 'now');
                INSERT INTO Chapters (Id, SourceId, ChapterIndex, Title) VALUES ('chapter', 'source', 0, 'Fixture');
                INSERT INTO LocalChapterContents (ChapterId, StartOffset, Length) VALUES ('chapter', 0, 1);
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM Books b JOIN BookSources s ON s.Id = b.ActiveSourceId AND s.BookId = b.Id JOIN Chapters c ON c.SourceId = s.Id JOIN LocalChapterContents l ON l.ChapterId = c.Id;"));
        await AssertIntegrityAsync(connection);
    }

    [Fact]
    public async Task Version_11_upgrade_preserves_identities_snapshots_progress_plans_cache_and_files()
    {
        using var directory = new TemporaryDirectory();
        var factory = await CreateVersion11FixtureAsync(directory.Path);
        var contentPath = Path.Combine(directory.Path, "Books", "book-a", "content.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
        await File.WriteAllTextAsync(contentPath, "sanitized fixture content");
        var filesBefore = Directory.GetFiles(directory.Path, "content.txt", SearchOption.AllDirectories);

        var snapshots = new Dictionary<string, string[]>();
        await using (var before = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            foreach (var table in PreservedTables)
                snapshots[table] = await SnapshotAsync(before, $"SELECT * FROM {table} ORDER BY 1, 2;");
            snapshots["Books"] = await SnapshotAsync(before,
                "SELECT Id, Title, Author, Description, ImportedAt, LastPlayedAt, UpdatedAt FROM Books ORDER BY Id;");
            snapshots["Sources"] = await SnapshotAsync(before,
                "SELECT Id, Title, Author, Description, ImportedAt, UpdatedAt FROM Books ORDER BY Id;");
            snapshots["Local"] = await SnapshotAsync(before,
                "SELECT Id, OriginalFileName, StoredFilePath, SourceHash, Encoding, ImportedAt, COALESCE(LastImportedAt, ImportedAt) FROM Books ORDER BY Id;");
            snapshots["Chapters"] = await SnapshotAsync(before,
                "SELECT Id, BookId, ChapterIndex, SortOrder, Title, StartOffset, Length FROM Chapters ORDER BY Id;");
        }

        var runner = CreateVersion12Runner(factory);
        await runner.InitializeAsync(CancellationToken.None);
        await runner.InitializeAsync(CancellationToken.None);
        await using var after = await factory.OpenConnectionAsync(CancellationToken.None);
        foreach (var table in PreservedTables)
            Assert.Equal(snapshots[table], await SnapshotAsync(after, $"SELECT * FROM {table} ORDER BY 1, 2;"));
        Assert.Equal(snapshots["Books"], await SnapshotAsync(after,
            "SELECT Id, Title, Author, Description, ImportedAt, LastPlayedAt, UpdatedAt FROM Books ORDER BY Id;"));
        Assert.Equal(snapshots["Sources"], await SnapshotAsync(after,
            "SELECT BookId, Title, Author, Description, CreatedAt, UpdatedAt FROM BookSources ORDER BY BookId;"));
        Assert.Equal(snapshots["Local"], await SnapshotAsync(after,
            "SELECT s.BookId, l.OriginalFileName, l.StoredContentPath, l.SourceHash, l.Encoding, l.ImportedAt, l.LastImportedAt FROM LocalBookSources l JOIN BookSources s ON s.Id = l.SourceId ORDER BY s.BookId;"));
        Assert.Equal(snapshots["Chapters"], await SnapshotAsync(after,
            "SELECT c.Id, s.BookId, c.ChapterIndex, c.SortOrder, c.Title, l.StartOffset, l.Length FROM Chapters c JOIN BookSources s ON s.Id = c.SourceId JOIN LocalChapterContents l ON l.ChapterId = c.Id ORDER BY c.Id;"));
        Assert.Equal(3L, await ScalarAsync(after,
            "SELECT COUNT(*) FROM Books b JOIN BookSources s ON s.Id = b.ActiveSourceId AND s.BookId = b.Id JOIN LocalBookSources l ON l.SourceId = s.Id WHERE s.SourceType = 1;"));
        Assert.Equal(3L, await ScalarAsync(after, "SELECT COUNT(*) FROM BookSources;"));
        await AssertIntegrityAsync(after);
        Assert.Equal("sanitized fixture content", await File.ReadAllTextAsync(contentPath));
        Assert.Equal(filesBefore, Directory.GetFiles(directory.Path, "content.txt", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Source_and_catalog_constraints_enforce_ownership_and_cascade_without_fallback()
    {
        using var directory = new TemporaryDirectory();
        var factory = await CreateVersion11FixtureAsync(directory.Path);
        await CreateVersion12Runner(factory).InitializeAsync(CancellationToken.None);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection,
            "INSERT INTO BookSources (Id, BookId, SourceType, Title, CreatedAt, UpdatedAt) VALUES ('extra', 'book-a', 1, 'test', 'now', 'now');"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection,
            "UPDATE Books SET ActiveSourceId = (SELECT ActiveSourceId FROM Books WHERE Id = 'book-b') WHERE Id = 'book-a';"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection,
            "INSERT INTO Chapters (Id, SourceId, ChapterIndex, Title) SELECT 'duplicate', SourceId, ChapterIndex, Title FROM Chapters WHERE Id = 'chapter-a';"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection,
            "UPDATE LocalChapterContents SET StartOffset = -1 WHERE ChapterId = 'chapter-a';"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection,
            "UPDATE LocalChapterContents SET Length = 0 WHERE ChapterId = 'chapter-a';"));

        await ExecuteAsync(connection, "DELETE FROM BookSources WHERE BookId = 'book-a';");
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Books WHERE Id = 'book-a' AND ActiveSourceId IS NULL AND Title = 'Fixture A';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM ReadingProgress WHERE BookId = 'book-a';"));
        foreach (var table in new[] { "Chapters", "LocalChapterContents", "ChapterSpeechPlans", "ChapterSpeechPlanSegments", "AudioCacheEntries" })
            Assert.Equal(0L, await ScalarAsync(connection, $"SELECT COUNT(*) FROM {table};"));
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT COUNT(*) FROM LocalBookSources;"));

        await ExecuteAsync(connection, "DELETE FROM Books;");
        foreach (var table in new[] { "Books", "BookSources", "LocalBookSources", "ReadingProgress" })
            Assert.Equal(0L, await ScalarAsync(connection, $"SELECT COUNT(*) FROM {table};"));
        await AssertIntegrityAsync(connection);
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("callback")]
    [InlineData("cancel")]
    public async Task Failed_version_12_rebuild_rolls_back_all_schema_data_and_version(string failure)
    {
        using var directory = new TemporaryDirectory();
        var factory = await CreateVersion11FixtureAsync(directory.Path);
        string[] schemaBefore;
        var snapshots = new Dictionary<string, string[]>();
        await using (var before = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            schemaBefore = await SnapshotAsync(before, "SELECT type, name, sql FROM sqlite_master ORDER BY name;");
            foreach (var table in PreservedTables.Concat(["Books", "Chapters", "SchemaVersion"]))
                snapshots[table] = await SnapshotAsync(before, $"SELECT * FROM {table} ORDER BY 1;");
        }

        using var cancellation = new CancellationTokenSource();
        var migration = SqliteMigrationRunner.AllMigrations.Single(m => m.Version == 12);
        migration = failure == "sql"
            ? migration with { Sql = migration.Sql + "\nINSERT INTO MissingMigrationTable VALUES (1);" }
            : migration with
            {
                ApplyDataAsync = (_, _, _) =>
                {
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

        await using (var after = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            Assert.Equal(schemaBefore, await SnapshotAsync(after, "SELECT type, name, sql FROM sqlite_master ORDER BY name;"));
            foreach (var (table, rows) in snapshots)
                Assert.Equal(rows, await SnapshotAsync(after, $"SELECT * FROM {table} ORDER BY 1;"));
            await AssertIntegrityAsync(after);
        }

        // A failed attempt must not prevent a subsequent ordinary startup from completing the migration.
        await CreateVersion12Runner(factory).InitializeAsync(CancellationToken.None);
        await using var retried = await factory.OpenConnectionAsync(CancellationToken.None);
        Assert.Equal(12L, await ScalarAsync(retried, "SELECT MAX(Version) FROM SchemaVersion;"));
        await AssertIntegrityAsync(retried);
    }

    [Fact]
    public async Task Invalid_legacy_relationship_aborts_rebuild_instead_of_committing_corrupt_schema()
    {
        using var directory = new TemporaryDirectory();
        var factory = await CreateVersion11FixtureAsync(directory.Path);
        await using (var connection = await factory.OpenConnectionAsync(CancellationToken.None))
            await ExecuteAsync(connection,
                "PRAGMA foreign_keys=OFF; INSERT INTO Chapters (Id, BookId, ChapterIndex, Title, StartOffset, Length) VALUES ('orphan', 'missing', 0, 'fixture', 0, 1); PRAGMA foreign_keys=ON;");

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateVersion12Runner(factory).InitializeAsync(CancellationToken.None));
        await using var after = await factory.OpenConnectionAsync(CancellationToken.None);
        Assert.Equal(11L, await ScalarAsync(after, "SELECT MAX(Version) FROM SchemaVersion;"));
        Assert.Equal(0L, await ScalarAsync(after, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'BookSources';"));
        Assert.Equal(1L, await ScalarAsync(after, "SELECT COUNT(*) FROM Chapters WHERE Id = 'orphan' AND BookId = 'missing';"));
    }

    // v12 remains an append-only, independently verified historical migration.
    private static SqliteMigrationRunner CreateVersion12Runner(ISqliteConnectionFactory factory) =>
        new(factory, SqliteMigrationRunner.AllMigrations.Where(m => m.Version <= 12).ToArray());

    internal static async Task<SqliteConnectionFactory> CreateFactoryAsync(string root)
    {
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        return new SqliteConnectionFactory(directories, observability: null, pooling: false);
    }

    internal static async Task<SqliteConnectionFactory> CreateVersion11FixtureAsync(string root)
    {
        var factory = await CreateFactoryAsync(root);
        await new SqliteMigrationRunner(factory, SqliteMigrationRunner.AllMigrations.Where(m => m.Version <= 11).ToArray())
            .InitializeAsync(CancellationToken.None);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        await ExecuteAsync(connection,
            """
            INSERT INTO Books (Id, Title, Author, Description, OriginalFileName, StoredFilePath, SourceHash, Encoding, ImportedAt, LastImportedAt, LastPlayedAt, UpdatedAt)
            VALUES ('book-a', 'Fixture A', 'Author A', 'Description A', 'fixture-a.txt', 'Books/book-a/content.txt', 'hash-a', 'utf-8', '2026-01-01', '2026-02-01', '2026-03-01', '2026-04-01'),
                   ('book-b', 'Fixture B', NULL, NULL, 'fixture-b.txt', 'Books/book-b/content.txt', 'hash-b', 'gb18030', '2026-01-02', NULL, NULL, '2026-04-02'),
                   ('empty-book', 'Empty', '', '', 'empty.txt', 'Books/empty-book/content.txt', 'hash-empty', 'utf-8', '2026-01-03', NULL, NULL, '2026-04-03');
            INSERT INTO Chapters (Id, BookId, ChapterIndex, SortOrder, Title, StartOffset, Length)
            VALUES ('chapter-a', 'book-a', 0, 7, 'Fixture chapter', 12, 34);
            INSERT INTO ReadingProgress (BookId, ChapterIndex, SegmentIndex, CharacterOffset, AudioPositionMilliseconds, UpdatedAt)
            VALUES ('book-a', 0, 2, 3, 4567, '2026-03-01');
            INSERT INTO ChapterSpeechPlans (ChapterId, ChapterRevisionHash, TextProfileFingerprint, PlanOutputHash, State, BodySegmentCount, UpdatedAt)
            VALUES ('chapter-a', X'01', X'02', X'03', 1, 1, '2026-03-01');
            INSERT INTO ChapterSpeechPlanSegments (ChapterId, OrderIndex, SegmentKind, SourceStartOffset, SourceLength, SpeechTextHash)
            VALUES ('chapter-a', 0, 0, 12, 34, X'04');
            INSERT INTO SynthesisProfiles (Fingerprint, SchemaVersion, RuleId, RuleFingerprint, SpeakSpeed, CreatedAt)
            VALUES (X'05', 1, 7, X'06', 0, '2026-03-01');
            INSERT INTO AudioCacheEntries (CacheKey, BookId, ChapterId, SourceStartOffset, SourceLength, SpeechTextHash, SynthesisProfileFingerprint, FilePath, ContentType, FileSize, DurationMilliseconds, HealthState, ValidatedAt, CreatedAt, LastAccessedAt)
            VALUES (X'07', 'book-a', 'chapter-a', 12, 34, X'04', X'05', 'Cache/fixture.mp3', 'audio/mpeg', 123, 4567, 1, '2026-03-01', '2026-03-01', '2026-03-02');
            INSERT INTO BookOperations (OperationId, Kind, Phase, BookId, PathsJson, CreatedAt, UpdatedAt)
            VALUES ('operation', 'Import', 'Prepared', 'book-a', '[]', '2026-01-01', '2026-01-01');
            """);
        return factory;
    }

    internal static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    internal static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(CancellationToken.None);
    }

    internal static async Task<string[]> SnapshotAsync(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var rows = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            var values = Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetValue(index) switch
            {
                DBNull => null,
                byte[] bytes => Convert.ToHexString(bytes),
                var value => Convert.ToString(value, CultureInfo.InvariantCulture)
            });
            rows.Add(System.Text.Json.JsonSerializer.Serialize(values));
        }

        return rows.ToArray();
    }

    internal static async Task AssertIntegrityAsync(SqliteConnection connection)
    {
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys;"));
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }
}
