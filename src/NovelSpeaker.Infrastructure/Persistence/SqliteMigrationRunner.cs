using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;

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
