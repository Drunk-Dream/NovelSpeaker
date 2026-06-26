using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.UnitTests.Persistence;

public sealed class SqliteMigrationRunnerTests
{
    [Fact]
    public async Task InitializeAsync_creates_current_schema_as_version_1()
    {
        var factory = await CreateInitializedFactoryAsync();

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var tableCommand = connection.CreateCommand();
        tableCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('SchemaVersion', 'AppMetadata', 'Books', 'Chapters', 'ChapterRules', 'HttpTtsRules', 'ReadingProgress', 'AudioCacheEntries');
            """;

        var tableCount = Convert.ToInt32(await tableCommand.ExecuteScalarAsync(CancellationToken.None));

        var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(CancellationToken.None));

        Assert.Equal(8, tableCount);
        Assert.Equal(1, version);
    }

    [Fact]
    public async Task InitializeAsync_creates_latest_book_columns_and_audio_cache_indexes()
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
        Assert.Equal(1, version);
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
}
