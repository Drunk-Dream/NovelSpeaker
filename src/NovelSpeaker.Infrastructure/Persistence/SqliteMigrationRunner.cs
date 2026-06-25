using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Speech.Rules;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Applies explicit schema migrations to the local SQLite database.
/// </summary>
public sealed class SqliteMigrationRunner : IDatabaseInitializer
{
    private static readonly SqliteMigration[] Migrations =
    [
        new(
            1,
            """
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version INTEGER NOT NULL PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS AppMetadata (
                Key TEXT NOT NULL PRIMARY KEY,
                Value TEXT NULL
            );
            """),
        new(
            2,
            """
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

            CREATE UNIQUE INDEX IX_Books_SourceHash
                ON Books(SourceHash);

            CREATE TABLE Chapters (
                Id TEXT NOT NULL PRIMARY KEY,
                BookId TEXT NOT NULL,
                ChapterIndex INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                StartOffset INTEGER NOT NULL CHECK(StartOffset >= 0),
                Length INTEGER NOT NULL CHECK(Length > 0),
                FOREIGN KEY(BookId) REFERENCES Books(Id) ON DELETE CASCADE,
                UNIQUE(BookId, ChapterIndex)
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

            CREATE INDEX IX_ChapterRules_SortOrder
                ON ChapterRules(SortOrder);
            """),
        new(
            3,
            """
            ALTER TABLE Books ADD COLUMN LastImportedAt TEXT NULL;
            ALTER TABLE Books ADD COLUMN LastPlayedAt TEXT NULL;
            UPDATE Books
            SET LastImportedAt = ImportedAt
            WHERE LastImportedAt IS NULL;

            UPDATE Chapters
            SET SortOrder = ChapterIndex
            WHERE SortOrder = 0;
            """),
        new(
            4,
            """
            CREATE TABLE HttpTtsRules (
                Id INTEGER NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                RuleJson TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL,
                CompatibilityStatus INTEGER NOT NULL,
                UnsupportedFieldsJson TEXT NOT NULL,
                LastUsedAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """),
        new(
            5,
            """
            SELECT 1;
            """),
        new(
            6,
            """
            CREATE TABLE ReadingProgress (
                BookId TEXT NOT NULL PRIMARY KEY,
                ChapterIndex INTEGER NOT NULL,
                SegmentIndex INTEGER NOT NULL,
                CharacterOffset INTEGER NOT NULL,
                AudioPositionMilliseconds INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY(BookId) REFERENCES Books(Id) ON DELETE CASCADE
            );
            """),
        new(
            7,
            """
            CREATE TABLE AudioCacheEntries (
                CacheKey TEXT NOT NULL PRIMARY KEY,
                BookId TEXT NOT NULL,
                ChapterIndex INTEGER NOT NULL,
                SegmentIndex INTEGER NOT NULL,
                RuleId INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                ContentType TEXT NULL,
                FileSize INTEGER NOT NULL CHECK(FileSize >= 0),
                DurationMilliseconds INTEGER NULL,
                CreatedAt TEXT NOT NULL,
                LastAccessedAt TEXT NOT NULL,
                Status INTEGER NOT NULL
            );

            CREATE INDEX IX_AudioCacheEntries_BookId_ChapterIndex
                ON AudioCacheEntries(BookId, ChapterIndex);

            CREATE INDEX IX_AudioCacheEntries_LastAccessedAt
                ON AudioCacheEntries(LastAccessedAt);
            """)
    ];

    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteMigrationRunner(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureMigrationTableAsync(connection, cancellationToken);

        var currentVersion = await GetCurrentVersionAsync(connection, cancellationToken);

        foreach (var migration in Migrations.Where(migration => migration.Version > currentVersion))
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);

            var versionCommand = connection.CreateCommand();
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "INSERT INTO SchemaVersion (Version) VALUES ($version);";
            versionCommand.Parameters.AddWithValue("$version", migration.Version);
            await versionCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        if (currentVersion < 5)
        {
            await UpgradeStoredTtsRulesAsync(connection, cancellationToken);
        }
    }

    private static async Task UpgradeStoredTtsRulesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var select = connection.CreateCommand();
        select.CommandText = "SELECT Id, Name, RuleJson FROM HttpTtsRules;";

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rules = new List<(long Id, string Name, string RuleJson)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rules.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        }

        foreach (var rule in rules)
        {
            var metadata = RuleJsonMetadata.Parse(rule.RuleJson);
            var convertedRule = new NovelSpeaker.Domain.Speech.HttpTtsRule(
                rule.Id,
                rule.Name,
                metadata.Url,
                metadata.ContentType,
                metadata.ConcurrentRate,
                metadata.Header,
                metadata.RequestOptionsJson,
                metadata.EnabledCookieJar,
                metadata.LastUpdateTime,
                string.Empty,
                true,
                TtsRuleCompatibilityStatus.Compatible,
                [],
                null,
                string.Empty,
                string.Empty);

            var update = connection.CreateCommand();
            update.CommandText = "UPDATE HttpTtsRules SET RuleJson = $ruleJson WHERE Id = $id;";
            update.Parameters.AddWithValue("$id", rule.Id);
            update.Parameters.AddWithValue("$ruleJson", NovelSpeakerRuleJsonSerializer.Serialize(convertedRule));
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureMigrationTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version INTEGER NOT NULL PRIMARY KEY
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> GetCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
