using Microsoft.Data.Sqlite;
using System.Text.Json;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

/// <summary>
/// Stores imported HTTP TTS rules in SQLite.
/// </summary>
public sealed class TtsRuleRepository : ITtsRuleRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

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
                   RuleJson,
                   LoginInfoJson,
                   IsEnabled,
                   CompatibilityStatus,
                   UnsupportedFieldsJson,
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
                   RuleJson,
                   LoginInfoJson,
                   IsEnabled,
                   CompatibilityStatus,
                   UnsupportedFieldsJson,
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
                    RuleJson,
                    LoginInfoJson,
                    IsEnabled,
                    CompatibilityStatus,
                    UnsupportedFieldsJson,
                    LastUsedAt,
                    CreatedAt,
                    UpdatedAt)
                VALUES (
                    $name,
                    $ruleJson,
                    $loginInfoJson,
                    $isEnabled,
                    $compatibilityStatus,
                    $unsupportedFieldsJson,
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
                RuleJson = $ruleJson,
                LoginInfoJson = $loginInfoJson,
                IsEnabled = $isEnabled,
                CompatibilityStatus = $compatibilityStatus,
                UnsupportedFieldsJson = $unsupportedFieldsJson,
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
        command.Parameters.AddWithValue("$name", rule.Name);
        command.Parameters.AddWithValue("$ruleJson", rule.RuleJson);
        command.Parameters.AddWithValue("$loginInfoJson", (object?)rule.LoginInfoJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$isEnabled", rule.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$compatibilityStatus", (int)rule.CompatibilityStatus);
        command.Parameters.AddWithValue(
            "$unsupportedFieldsJson",
            JsonSerializer.Serialize(rule.UnsupportedFields, SerializerOptions));
        command.Parameters.AddWithValue("$lastUsedAt", (object?)rule.LastUsedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", rule.CreatedAt);
        command.Parameters.AddWithValue("$updatedAt", rule.UpdatedAt);
    }

    private static HttpTtsRule ReadRule(SqliteDataReader reader)
    {
        var ruleJson = reader.GetString(2);
        var metadata = RuleJsonMetadata.Parse(ruleJson);
        var unsupportedFieldsJson = reader.GetString(6);
        var unsupportedFields = JsonSerializer.Deserialize<string[]>(unsupportedFieldsJson, SerializerOptions) ?? [];

        return new HttpTtsRule(
            reader.GetInt64(0),
            reader.GetString(1),
            metadata.Url,
            metadata.ContentType,
            metadata.ConcurrentRate,
            metadata.Header,
            metadata.RequestOptionsJson,
            metadata.EnabledCookieJar,
            metadata.LastUpdateTime,
            ruleJson,
            reader.GetInt64(4) == 1,
            (TtsRuleCompatibilityStatus)reader.GetInt32(5),
            unsupportedFields,
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9))
        {
            LoginInfoJson = reader.IsDBNull(3) ? metadata.LoginInfoJson : reader.GetString(3)
        };
    }
}
