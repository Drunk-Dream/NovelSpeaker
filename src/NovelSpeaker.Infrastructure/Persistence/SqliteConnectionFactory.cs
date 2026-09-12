using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Observability;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Opens SQLite connections against the application database file.
/// </summary>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly IAppDataDirectoryProvider _directories;
    private readonly IObservability _observability;

    public SqliteConnectionFactory(IAppDataDirectoryProvider directories, IObservability? observability = null)
    {
        _directories = directories;
        _observability = observability ?? new ObservabilityHub(new ObservabilityContextAccessor());
        SqliteRuntimeInitializer.EnsureInitialized();
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var operation = _observability.StartOperation(OperationCatalog.StorageQuery);

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
            operation.Complete(OperationResult.Succeeded());
            return connection;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation.Complete(OperationResult.Cancelled());
            throw;
        }
        catch
        {
            operation.Complete(OperationResult.Failed("storage-open-failed"));
            await connection.DisposeAsync();
            throw;
        }
    }
}
