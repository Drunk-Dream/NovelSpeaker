using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Reads the current schema version from the SQLite database for diagnostics.
/// </summary>
public sealed class SqliteDatabaseSchemaVersionProvider : IDatabaseSchemaVersionProvider
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteDatabaseSchemaVersionProvider(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> GetCurrentVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }
}
