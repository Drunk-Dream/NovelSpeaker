using Microsoft.Data.Sqlite;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.UnitTests.Persistence;

public sealed class SqliteMigrationRunnerTests
{
    [Fact]
    public async Task InitializeAsync_creates_import_tables_and_advances_schema_version()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);

        await initializer.InitializeAsync(CancellationToken.None);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var tableCommand = connection.CreateCommand();
        tableCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('SchemaVersion', 'AppMetadata', 'Books', 'Chapters', 'ChapterRules');
            """;

        var tableCount = Convert.ToInt32(await tableCommand.ExecuteScalarAsync(CancellationToken.None));

        var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(CancellationToken.None));

        Assert.Equal(5, tableCount);
        Assert.Equal(3, version);
    }

    [Fact]
    public async Task InitializeAsync_creates_schema_version_and_metadata_tables()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);

        await initializer.InitializeAsync(CancellationToken.None);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('SchemaVersion', 'AppMetadata');";

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal(2, count);
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
        command.CommandText = "SELECT MAX(Version) FROM SchemaVersion;";

        var version = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal(3, version);
    }

    [Fact]
    public async Task InitializeAsync_upgrades_existing_version_2_schema_with_new_columns()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await using (var connection = new SqliteConnection($"Data Source={directories.DatabasePath}"))
        {
            await connection.OpenAsync(CancellationToken.None);
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE SchemaVersion (
                    Version INTEGER NOT NULL PRIMARY KEY
                );
                INSERT INTO SchemaVersion (Version) VALUES (2);

                CREATE TABLE Books (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Author TEXT NULL,
                    OriginalFileName TEXT NOT NULL,
                    StoredFilePath TEXT NOT NULL,
                    SourceHash TEXT NOT NULL,
                    Encoding TEXT NOT NULL,
                    ImportedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE Chapters (
                    Id TEXT NOT NULL PRIMARY KEY,
                    BookId TEXT NOT NULL,
                    ChapterIndex INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    Title TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    StartOffset INTEGER NOT NULL,
                    Length INTEGER NOT NULL
                );

                CREATE TABLE ChapterRules (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Pattern TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    IsEnabled INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """;

            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var repository = new ChapterRuleRepository(factory);
        var seeder = new DefaultChapterRuleSeeder(repository);
        var initializer = new StartupDatabaseInitializer(directories, runner, seeder);

        await initializer.InitializeAsync(CancellationToken.None);

        await using var upgradedConnection = await factory.OpenConnectionAsync(CancellationToken.None);
        var pragma = upgradedConnection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(Books);";
        await using var reader = await pragma.ExecuteReaderAsync(CancellationToken.None);
        var columns = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("LastImportedAt", columns);
        Assert.Contains("LastPlayedAt", columns);
    }
}
