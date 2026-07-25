using Microsoft.Data.Sqlite;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.UnitTests.Persistence;

public sealed class SqliteMigrationRunnerTests
{
    [Fact]
    public async Task InitializeAsync_creates_current_schema_as_version_6()
    {
        var factory = await CreateInitializedFactoryAsync();

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var tableCommand = connection.CreateCommand();
        tableCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('SchemaVersion', 'AppMetadata', 'Books', 'Chapters', 'ChapterRules', 'HttpTtsRules', 'ReadingProgress', 'AudioCacheEntries', 'RegexReplacementRules', 'BookOperations');
            """;

        var tableCount = Convert.ToInt32(await tableCommand.ExecuteScalarAsync(CancellationToken.None));

        var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(CancellationToken.None));

        Assert.Equal(10, tableCount);
        Assert.Equal(6, version);
    }

    [Fact]
    public async Task InitializeAsync_creates_latest_book_columns_audio_cache_indexes_and_tts_rule_columns()
    {
        var factory = await CreateInitializedFactoryAsync();

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var bookPragma = connection.CreateCommand();
        bookPragma.CommandText = "PRAGMA table_info(Books);";

        await using var reader = await bookPragma.ExecuteReaderAsync(CancellationToken.None);
        var columns = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("LastImportedAt", columns);
        Assert.Contains("LastPlayedAt", columns);

        var chapterPragma = connection.CreateCommand();
        chapterPragma.CommandText = "PRAGMA table_info(Chapters);";
        await using var chapterReader = await chapterPragma.ExecuteReaderAsync(CancellationToken.None);
        var chapterColumns = new List<string>();
        while (await chapterReader.ReadAsync(CancellationToken.None))
        {
            chapterColumns.Add(chapterReader.GetString(1));
        }

        Assert.DoesNotContain("Content", chapterColumns);

        var ttsRulePragma = connection.CreateCommand();
        ttsRulePragma.CommandText = "PRAGMA table_info(HttpTtsRules);";
        await using var ttsRuleReader = await ttsRulePragma.ExecuteReaderAsync(CancellationToken.None);
        var ttsRuleColumns = new List<string>();
        while (await ttsRuleReader.ReadAsync(CancellationToken.None))
        {
            ttsRuleColumns.Add(ttsRuleReader.GetString(1));
        }

        Assert.Contains("Url", ttsRuleColumns);
        Assert.Contains("RequestOptionsJson", ttsRuleColumns);
        Assert.DoesNotContain("RuleJson", ttsRuleColumns);
        Assert.DoesNotContain("CompatibilityStatus", ttsRuleColumns);

        var indexCommand = connection.CreateCommand();
        indexCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'index'
              AND name IN ('IX_AudioCacheEntries_BookId_ChapterIndex', 'IX_AudioCacheEntries_LastAccessedAt');
            """;

        var indexCount = Convert.ToInt32(await indexCommand.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal(2, indexCount);

        var regexIndexCommand = connection.CreateCommand();
        regexIndexCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_RegexReplacementRules_SortOrder';";
        Assert.Equal(1, Convert.ToInt32(await regexIndexCommand.ExecuteScalarAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task InitializeAsync_is_idempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);

        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";

        var version = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal(6, version);
    }

    [Fact]
    public async Task Path_migration_converts_valid_legacy_absolute_paths_and_leaves_unsafe_values_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var factory = new SqliteConnectionFactory(directories);
        await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
        var validPath = Path.Combine(directories.BooksDirectoryPath, "book-1", "content.txt");
        var unsafePath = Path.Combine(Path.GetTempPath(), "outside-content.txt");
        await using (var connection = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO Books
                    (Id, Title, OriginalFileName, StoredFilePath, SourceHash, Encoding, ImportedAt, UpdatedAt)
                VALUES
                    ('book-1', 'valid', 'valid.txt', $validPath, 'hash-1', 'utf-8', $now, $now),
                    ('book-2', 'unsafe', 'unsafe.txt', $unsafePath, 'hash-2', 'utf-8', $now, $now);
                """;
            command.Parameters.AddWithValue("$validPath", validPath);
            command.Parameters.AddWithValue("$unsafePath", unsafePath);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await new AppStoragePathMigrationService(factory, new AppStoragePathResolver(directories))
            .MigrateAsync(CancellationToken.None);

        await using var verification = await factory.OpenConnectionAsync(CancellationToken.None);
        var select = verification.CreateCommand();
        select.CommandText = "SELECT Id, StoredFilePath FROM Books ORDER BY Id;";
        await using var reader = await select.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("Books/book-1/content.txt", reader.GetString(1));
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(unsafePath, reader.GetString(1));
    }

    [Fact]
    public async Task InitializeAsync_rejects_unsupported_version_3_database()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={directories.DatabasePath}"))
        {
            await connection.OpenAsync(CancellationToken.None);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE SchemaVersion (
                    Version INTEGER NOT NULL PRIMARY KEY
                );

                INSERT INTO SchemaVersion (Version) VALUES (3);
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);

        var exception = await Assert.ThrowsAsync<IncompatibleDatabaseSchemaException>(
            () => runner.InitializeAsync(CancellationToken.None));
        Assert.Equal(3, exception.DetectedVersion);
        Assert.Equal(4, exception.MinimumSupportedVersion);
        Assert.Equal(6, exception.CurrentVersion);
        Assert.Equal(6, exception.RequiredVersion);
        Assert.Contains("支持版本 4 到 6", exception.Message, StringComparison.Ordinal);
        Assert.Contains("数据库未被修改", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_rejects_newer_version_7_database_without_changing_it()
    {
        var (factory, _) = await CreateDatabaseAtVersionAsync(7);
        var runner = new SqliteMigrationRunner(factory);

        var exception = await Assert.ThrowsAsync<IncompatibleDatabaseSchemaException>(
            () => runner.InitializeAsync(CancellationToken.None));

        Assert.Equal(7, exception.DetectedVersion);
        Assert.Equal(4, exception.MinimumSupportedVersion);
        Assert.Equal(6, exception.CurrentVersion);
        Assert.Equal(6, exception.RequiredVersion);
        Assert.Contains("支持版本 4 到 6", exception.Message, StringComparison.Ordinal);
        Assert.Contains("数据库未被修改", exception.Message, StringComparison.Ordinal);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(Version) FROM SchemaVersion;";
        Assert.Equal(7, Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task InitializeAsync_rolls_back_schema_data_and_version_when_migration_fails()
    {
        var (factory, _) = await CreateDatabaseAtVersionAsync(4);
        await using (var connection = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE MigrationMarker (Value TEXT NOT NULL);
                INSERT INTO MigrationMarker (Value) VALUES ('before');
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var runner = new SqliteMigrationRunner(
            factory,
            [
                new SqliteMigration(
                    5,
                    """
                    CREATE TABLE PartialMigration (Id INTEGER NOT NULL PRIMARY KEY);
                    UPDATE MigrationMarker SET Value = 'during';
                    INSERT INTO MissingTable (Value) VALUES ('fail');
                    """)
            ]);

        await Assert.ThrowsAsync<SqliteException>(() => runner.InitializeAsync(CancellationToken.None));

        await using var verification = await factory.OpenConnectionAsync(CancellationToken.None);
        var tableCommand = verification.CreateCommand();
        tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'PartialMigration';";
        Assert.Equal(0, Convert.ToInt32(await tableCommand.ExecuteScalarAsync(CancellationToken.None)));

        var dataCommand = verification.CreateCommand();
        dataCommand.CommandText = "SELECT Value FROM MigrationMarker;";
        Assert.Equal("before", Convert.ToString(await dataCommand.ExecuteScalarAsync(CancellationToken.None)));

        var versionCommand = verification.CreateCommand();
        versionCommand.CommandText = "SELECT MAX(Version) FROM SchemaVersion;";
        Assert.Equal(4, Convert.ToInt32(await versionCommand.ExecuteScalarAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task Foreign_keys_reject_orphans_and_delete_books_cascades_chapters_and_progress()
    {
        var factory = await CreateInitializedFactoryAsync();

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var orphan = connection.CreateCommand();
        orphan.CommandText =
            """
            INSERT INTO Chapters (Id, BookId, ChapterIndex, SortOrder, Title, StartOffset, Length)
            VALUES ('orphan', 'missing', 0, 0, 'orphan', 0, 1);
            """;
        await Assert.ThrowsAsync<SqliteException>(() => orphan.ExecuteNonQueryAsync(CancellationToken.None));

        var seed = connection.CreateCommand();
        seed.CommandText =
            """
            INSERT INTO Books
                (Id, Title, OriginalFileName, StoredFilePath, SourceHash, Encoding, ImportedAt, UpdatedAt)
            VALUES
                ('book', 'book', 'book.txt', 'Books/book/content.txt', 'hash', 'utf-8', '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
            INSERT INTO Chapters (Id, BookId, ChapterIndex, SortOrder, Title, StartOffset, Length)
            VALUES ('chapter', 'book', 0, 0, 'chapter', 0, 1);
            INSERT INTO ReadingProgress
                (BookId, ChapterIndex, SegmentIndex, CharacterOffset, AudioPositionMilliseconds, UpdatedAt)
            VALUES
                ('book', 0, 0, 0, 0, '2026-01-01T00:00:00.0000000+00:00');
            DELETE FROM Books WHERE Id = 'book';
            """;
        await seed.ExecuteNonQueryAsync(CancellationToken.None);

        var count = connection.CreateCommand();
        count.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM Chapters WHERE BookId = 'book') +
                (SELECT COUNT(*) FROM ReadingProgress WHERE BookId = 'book');
            """;
        Assert.Equal(0, Convert.ToInt32(await count.ExecuteScalarAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task Concurrent_writer_waits_for_lock_then_succeeds_without_enabling_wal()
    {
        var factory = await CreateInitializedFactoryAsync();
        await using var lockConnection = await factory.OpenConnectionAsync(CancellationToken.None);
        await using var waitingConnection = await factory.OpenConnectionAsync(CancellationToken.None);

        var journalMode = lockConnection.CreateCommand();
        journalMode.CommandText = "PRAGMA journal_mode;";
        Assert.False(
            string.Equals(
                "wal",
                Convert.ToString(await journalMode.ExecuteScalarAsync(CancellationToken.None)),
                StringComparison.OrdinalIgnoreCase));

        await using var transaction = await lockConnection.BeginTransactionAsync(CancellationToken.None);
        var lockCommand = lockConnection.CreateCommand();
        lockCommand.Transaction = (SqliteTransaction)transaction;
        lockCommand.CommandText = "INSERT INTO AppMetadata (Key, Value) VALUES ('lock', 'held');";
        await lockCommand.ExecuteNonQueryAsync(CancellationToken.None);

        var commandStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitingWrite = Task.Run(
            async () =>
            {
                var command = waitingConnection.CreateCommand();
                command.CommandText = "INSERT INTO AppMetadata (Key, Value) VALUES ('waiting', 'released');";
                commandStarted.SetResult();
                return await command.ExecuteNonQueryAsync(CancellationToken.None);
            });

        await commandStarted.Task;
        await Task.Yield();
        Assert.False(waitingWrite.IsCompleted);

        await transaction.CommitAsync(CancellationToken.None);
        Assert.Equal(1, await waitingWrite.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task InitializeAsync_honors_pre_cancelled_token()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.InitializeAsync(cancellation.Token));
    }

    private static async Task<SqliteConnectionFactory> CreateInitializedFactoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);

        await initializer.InitializeAsync(CancellationToken.None);
        return factory;
    }

    private static async Task<(SqliteConnectionFactory Factory, LocalAppDataDirectoryProvider Directories)> CreateDatabaseAtVersionAsync(int version)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var factory = new SqliteConnectionFactory(directories);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE SchemaVersion (
                Version INTEGER NOT NULL PRIMARY KEY
            );
            INSERT INTO SchemaVersion (Version) VALUES ($version);
            """;
        command.Parameters.AddWithValue("$version", version);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        return (factory, directories);
    }
}
