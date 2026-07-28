using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Lazily replaces valid legacy absolute database paths with root-relative storage keys.
/// </summary>
public sealed class AppStoragePathMigrationService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IAppStoragePathResolver _pathResolver;

    public AppStoragePathMigrationService(
        ISqliteConnectionFactory connectionFactory,
        IAppStoragePathResolver pathResolver)
    {
        _connectionFactory = connectionFactory;
        _pathResolver = pathResolver;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await MigrateTableAsync(
            connection,
            transaction,
            "Books",
            "Id",
            "StoredFilePath",
            cancellationToken).ConfigureAwait(false);
        await MigrateTableAsync(
            connection,
            transaction,
            "AudioCacheEntries",
            "CacheKey",
            "FilePath",
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MigrateTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string keyColumn,
        string pathColumn,
        CancellationToken cancellationToken)
    {
        var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = $"SELECT {keyColumn}, {pathColumn} FROM {tableName};";
        var updates = new List<(object Id, string StorageKey)>();
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var persistedPath = reader.GetString(1);
                if (!Path.IsPathFullyQualified(persistedPath))
                {
                    continue;
                }

                try
                {
                    updates.Add((reader.GetValue(0), _pathResolver.GetStorageKey(persistedPath)));
                }
                catch (Exception exception) when (IsInvalidPersistedPath(exception))
                {
                    // Leave unsafe legacy values untouched so consumers continue to reject them without external access.
                }
            }
        }

        foreach (var update in updates)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"UPDATE {tableName} SET {pathColumn} = $storageKey WHERE {keyColumn} = $id;";
            command.Parameters.AddWithValue("$storageKey", update.StorageKey);
            command.Parameters.AddWithValue("$id", update.Id);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsInvalidPersistedPath(Exception exception) =>
        exception is InvalidDataException or ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException;
}
