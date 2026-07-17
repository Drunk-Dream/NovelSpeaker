using System.Text.Json;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>
/// Stores recoverable book operations in SQLite independently of their file-system phase.
/// </summary>
public sealed class SqliteBookOperationJournal : IBookOperationJournal
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public SqliteBookOperationJournal(ISqliteConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task CreateAsync(BookOperationRecord operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO BookOperations (OperationId, Kind, Phase, BookId, PathsJson, CreatedAt, UpdatedAt)
            VALUES ($operationId, $kind, $phase, $bookId, $pathsJson, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$operationId", operation.OperationId);
        command.Parameters.AddWithValue("$kind", operation.Kind.ToString());
        command.Parameters.AddWithValue("$phase", operation.Phase.ToString());
        command.Parameters.AddWithValue("$bookId", operation.BookId);
        command.Parameters.AddWithValue("$pathsJson", JsonSerializer.Serialize(operation.Paths));
        command.Parameters.AddWithValue("$createdAt", SqliteDateTimeMapper.Format(operation.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqliteDateTimeMapper.Format(_timeProvider.GetUtcNow()));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPhaseAsync(string operationId, BookOperationPhase phase, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE BookOperations
            SET Phase = $phase, UpdatedAt = $updatedAt
            WHERE OperationId = $operationId;
            """;
        command.Parameters.AddWithValue("$operationId", operationId);
        command.Parameters.AddWithValue("$phase", phase.ToString());
        command.Parameters.AddWithValue("$updatedAt", SqliteDateTimeMapper.Format(_timeProvider.GetUtcNow()));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("未找到要推进的书籍操作记录。");
        }
    }

    public async Task<IReadOnlyList<BookOperationRecord>> GetIncompleteAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT OperationId, Kind, Phase, BookId, PathsJson, CreatedAt
            FROM BookOperations
            WHERE Phase <> $completed
            ORDER BY CreatedAt, OperationId;
            """;
        command.Parameters.AddWithValue("$completed", BookOperationPhase.Completed.ToString());

        var operations = new List<BookOperationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var paths = JsonSerializer.Deserialize<BookOperationPath[]>(reader.GetString(4))
                ?? throw new InvalidDataException("书籍操作记录缺少路径数据。");
            operations.Add(new BookOperationRecord(
                reader.GetString(0),
                Enum.Parse<BookOperationKind>(reader.GetString(1), ignoreCase: false),
                Enum.Parse<BookOperationPhase>(reader.GetString(2), ignoreCase: false),
                reader.GetString(3),
                paths,
                SqliteDateTimeMapper.Parse(reader.GetString(5))));
        }

        return operations;
    }
}
