using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Opens SQLite connections against the application database file.
/// </summary>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly IAppDataDirectoryProvider _directories;

    public SqliteConnectionFactory(IAppDataDirectoryProvider directories)
    {
        _directories = directories;
        SqliteRuntimeInitializer.EnsureInitialized();
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new SqliteConnection($"Data Source={_directories.DatabasePath}")
        {
            DefaultTimeout = 5
        };

        try
        {
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA foreign_keys=ON;
                PRAGMA busy_timeout=5000;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
