using Microsoft.Data.Sqlite;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Opens SQLite connections for startup and repository operations.
/// </summary>
public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}
