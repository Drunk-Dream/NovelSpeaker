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
