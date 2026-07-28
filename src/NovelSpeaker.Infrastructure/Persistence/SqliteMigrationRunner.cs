using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Applies explicit schema migrations to the local SQLite database.
/// </summary>
public sealed class SqliteMigrationRunner : IDatabaseInitializer
{
    private const int MinimumSupportedVersion = 4;
    private const int CurrentSchemaVersion = 7;
    private static readonly SqliteMigration[] Migrations =
    [
        new(
            4,
            """
            CREATE TABLE AppMetadata (
                Key TEXT NOT NULL PRIMARY KEY,
                Value TEXT NULL
            );

            CREATE TABLE Books (
                Id TEXT NOT NULL PRIMARY KEY,
                Title TEXT NOT NULL,
                Author TEXT NULL,
                OriginalFileName TEXT NOT NULL,
                StoredFilePath TEXT NOT NULL,
                SourceHash TEXT NOT NULL,
                Encoding TEXT NOT NULL,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                LastImportedAt TEXT NULL,
                LastPlayedAt TEXT NULL
            );

            CREATE UNIQUE INDEX IX_Books_SourceHash
                ON Books(SourceHash);

            CREATE TABLE Chapters (
                Id TEXT NOT NULL PRIMARY KEY,
                BookId TEXT NOT NULL,
                ChapterIndex INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                Title TEXT NOT NULL,
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

            CREATE TABLE HttpTtsRules (
                Id INTEGER NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                Url TEXT NOT NULL,
                ContentType TEXT NULL,
                ConcurrentRate TEXT NULL,
                Header TEXT NULL,
                RequestOptionsJson TEXT NULL,
                LastUpdateTime INTEGER NULL,
                IsEnabled INTEGER NOT NULL,
                LastUsedAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE ReadingProgress (
                BookId TEXT NOT NULL PRIMARY KEY,
                ChapterIndex INTEGER NOT NULL,
                SegmentIndex INTEGER NOT NULL,
                CharacterOffset INTEGER NOT NULL,
                AudioPositionMilliseconds INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY(BookId) REFERENCES Books(Id) ON DELETE CASCADE
            );

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
            """),
        new(
            5,
            """
            CREATE TABLE RegexReplacementRules (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL,
                Pattern TEXT NOT NULL,
                Replacement TEXT NOT NULL,
                Scope TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IX_RegexReplacementRules_SortOrder
                ON RegexReplacementRules(SortOrder);
            """),
        new(
            6,
            """
            CREATE TABLE BookOperations (
                OperationId TEXT NOT NULL PRIMARY KEY,
                Kind TEXT NOT NULL,
                Phase TEXT NOT NULL,
                BookId TEXT NOT NULL,
                PathsJson TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IX_BookOperations_Phase_CreatedAt
                ON BookOperations(Phase, CreatedAt);
            """),
        new(
            7,
            """
            DROP INDEX IF EXISTS IX_AudioCacheEntries_BookId_ChapterIndex;
            DROP INDEX IF EXISTS IX_AudioCacheEntries_LastAccessedAt;
            ALTER TABLE AudioCacheEntries RENAME TO AudioCacheEntries_V6_Discarded;

            CREATE TABLE ChapterSpeechPlans (
                ChapterId TEXT NOT NULL PRIMARY KEY,
                ChapterRevisionHash BLOB NOT NULL,
                TextProfileFingerprint BLOB NOT NULL,
                PlanOutputHash BLOB NOT NULL,
                State INTEGER NOT NULL,
                BodySegmentCount INTEGER NOT NULL CHECK(BodySegmentCount >= 0),
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY(ChapterId) REFERENCES Chapters(Id) ON DELETE CASCADE
            );

            CREATE TABLE ChapterSpeechPlanSegments (
                ChapterId TEXT NOT NULL,
                OrderIndex INTEGER NOT NULL,
                SegmentKind INTEGER NOT NULL,
                SourceStartOffset INTEGER NOT NULL CHECK(SourceStartOffset >= 0),
                SourceLength INTEGER NOT NULL CHECK(SourceLength > 0),
                SpeechTextHash BLOB NOT NULL,
                PRIMARY KEY(ChapterId, OrderIndex),
                UNIQUE(ChapterId, SegmentKind, SourceStartOffset, SourceLength),
                FOREIGN KEY(ChapterId) REFERENCES ChapterSpeechPlans(ChapterId) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE TABLE SynthesisProfiles (
                Fingerprint BLOB NOT NULL PRIMARY KEY,
                SchemaVersion INTEGER NOT NULL,
                RuleId INTEGER NOT NULL,
                RuleFingerprint BLOB NOT NULL,
                SpeakSpeed INTEGER NOT NULL,
                OptionsJson TEXT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE AudioCacheEntries (
                CacheKey BLOB NOT NULL PRIMARY KEY,
                KeyVersion INTEGER NOT NULL DEFAULT 1,
                BookId TEXT NOT NULL,
                ChapterId TEXT NOT NULL,
                SegmentKind INTEGER NOT NULL DEFAULT 0,
                SourceStartOffset INTEGER NOT NULL DEFAULT 0,
                SourceLength INTEGER NOT NULL DEFAULT 1 CHECK(SourceLength > 0),
                SpeechTextHash BLOB NOT NULL,
                SynthesisProfileFingerprint BLOB NOT NULL,
                FilePath TEXT NOT NULL,
                ContentType TEXT NULL,
                FileSize INTEGER NOT NULL CHECK(FileSize >= 0),
                DurationMilliseconds INTEGER NULL,
                HealthState INTEGER NOT NULL DEFAULT 1,
                ValidatedAt TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                LastAccessedAt TEXT NOT NULL,
                FOREIGN KEY(BookId) REFERENCES Books(Id) ON DELETE CASCADE,
                FOREIGN KEY(ChapterId) REFERENCES Chapters(Id) ON DELETE CASCADE,
                FOREIGN KEY(SynthesisProfileFingerprint) REFERENCES SynthesisProfiles(Fingerprint)
            );

            CREATE INDEX IX_AudioCacheEntries_BookId_ChapterId
                ON AudioCacheEntries(BookId, ChapterId);

            CREATE INDEX IX_AudioCacheEntries_CurrentConfiguration
                ON AudioCacheEntries(
                    ChapterId,
                    SynthesisProfileFingerprint,
                    SegmentKind,
                    SourceStartOffset,
                    SourceLength,
                    SpeechTextHash,
                    HealthState);

            CREATE INDEX IX_AudioCacheEntries_LastAccessedAt
                ON AudioCacheEntries(LastAccessedAt);

            DROP TABLE AudioCacheEntries_V6_Discarded;
            INSERT OR IGNORE INTO AppMetadata (Key, Value)
            VALUES ('AudioCacheV7ResetPending', '1');
            """)
    ];

    internal static IReadOnlyList<SqliteMigration> AllMigrations => Migrations;

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IReadOnlyList<SqliteMigration> _migrations;

    public SqliteMigrationRunner(ISqliteConnectionFactory connectionFactory)
        : this(connectionFactory, Migrations)
    {
    }

    internal SqliteMigrationRunner(
        ISqliteConnectionFactory connectionFactory,
        IReadOnlyList<SqliteMigration> migrations)
    {
        _connectionFactory = connectionFactory;
        _migrations = migrations;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureMigrationTableAsync(connection, cancellationToken);

        var currentVersion = await GetCurrentVersionAsync(connection, cancellationToken);
        if (currentVersion > 0 &&
            (currentVersion < MinimumSupportedVersion || currentVersion > CurrentSchemaVersion))
        {
            throw new IncompatibleDatabaseSchemaException(
                currentVersion,
                MinimumSupportedVersion,
                CurrentSchemaVersion);
        }

        foreach (var migration in _migrations.Where(migration => migration.Version > currentVersion))
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
