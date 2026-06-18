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
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection($"Data Source={_directories.DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
