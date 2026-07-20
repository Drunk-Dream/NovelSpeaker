using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

/// <summary>
/// Stores imported HTTP TTS rules in SQLite.
/// </summary>
public sealed class TtsRuleRepository : ITtsRuleRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public TtsRuleRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<HttpTtsRule>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id,
                   Name,
                   Url,
                   ContentType,
                   ConcurrentRate,
                   Header,
                   RequestOptionsJson,
                   LastUpdateTime,
                   IsEnabled,
                   LastUsedAt,
                   CreatedAt,
                   UpdatedAt
            FROM HttpTtsRules
            ORDER BY
                CASE WHEN LastUsedAt IS NULL THEN 1 ELSE 0 END,
                LastUsedAt DESC,
                Name;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<HttpTtsRule>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadRule(reader));
        }

        return items;
    }

    public async Task<HttpTtsRule?> GetByIdAsync(long ruleId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id,
                   Name,
                   Url,
                   ContentType,
                   ConcurrentRate,
                   Header,
                   RequestOptionsJson,
                   LastUpdateTime,
                   IsEnabled,
                   LastUsedAt,
                   CreatedAt,
                   UpdatedAt
            FROM HttpTtsRules
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", ruleId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRule(reader)
            : null;
    }

    public async Task<long> SaveAsync(HttpTtsRule rule, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        if (rule.Id <= 0)
        {
            var insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO HttpTtsRules (
                    Name,
                    Url,
                    ContentType,
                    ConcurrentRate,
                    Header,
                    RequestOptionsJson,
                    LastUpdateTime,
                    IsEnabled,
                    LastUsedAt,
                    CreatedAt,
                    UpdatedAt)
                VALUES (
                    $name,
                    $url,
                    $contentType,
                    $concurrentRate,
                    $header,
                    $requestOptionsJson,
                    $lastUpdateTime,
                    $isEnabled,
                    $lastUsedAt,
                    $createdAt,
                    $updatedAt);
                SELECT last_insert_rowid();
                """;

            AddParameters(insert, rule);
            return Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
        }

        var update = connection.CreateCommand();
        update.CommandText =
            """
            UPDATE HttpTtsRules
            SET Name = $name,
                Url = $url,
                ContentType = $contentType,
                ConcurrentRate = $concurrentRate,
                Header = $header,
                RequestOptionsJson = $requestOptionsJson,
                LastUpdateTime = $lastUpdateTime,
                IsEnabled = $isEnabled,
                LastUsedAt = $lastUsedAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;

        update.Parameters.AddWithValue("$id", rule.Id);
        AddParameters(update, rule);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return rule.Id;
    }

    public async Task DeleteAsync(long ruleId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM HttpTtsRules WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", ruleId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(SqliteCommand command, HttpTtsRule rule)
    {
        var row = TtsRulePersistenceMapper.FromDomain(rule);
        command.Parameters.AddWithValue("$name", row.Name);
        command.Parameters.AddWithValue("$url", row.Url);
        command.Parameters.AddWithValue("$contentType", (object?)row.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue("$concurrentRate", (object?)row.ConcurrentRate ?? DBNull.Value);
        command.Parameters.AddWithValue("$header", (object?)row.Header ?? DBNull.Value);
        command.Parameters.AddWithValue("$requestOptionsJson", (object?)row.RequestOptionsJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastUpdateTime", (object?)row.LastUpdateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("$isEnabled", row.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$lastUsedAt", (object?)row.LastUsedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", row.CreatedAt);
        command.Parameters.AddWithValue("$updatedAt", row.UpdatedAt);
    }

    private static HttpTtsRule ReadRule(SqliteDataReader reader)
    {
        var row = new TtsRuleRow(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            reader.GetInt64(8) == 1,
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11));
        return TtsRulePersistenceMapper.ToDomain(row);
    }
}
