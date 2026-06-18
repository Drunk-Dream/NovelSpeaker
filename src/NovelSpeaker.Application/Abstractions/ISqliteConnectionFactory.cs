using Microsoft.Data.Sqlite;

namespace NovelSpeaker.Application.Abstractions;

/// <summary>
/// Opens SQLite connections for startup and repository operations.
/// </summary>
public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}
