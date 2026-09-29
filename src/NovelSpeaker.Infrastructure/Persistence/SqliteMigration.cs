using Microsoft.Data.Sqlite;

namespace NovelSpeaker.Infrastructure.Persistence;

internal sealed record SqliteMigration(
    int Version,
    string Sql,
    Func<SqliteConnection, SqliteTransaction, CancellationToken, Task>? ApplyDataAsync = null);
