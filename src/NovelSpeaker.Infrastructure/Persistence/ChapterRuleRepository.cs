using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books;
using Microsoft.Data.Sqlite;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Stores global chapter rules in SQLite.
/// </summary>
public sealed class ChapterRuleRepository : IChapterRuleRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public ChapterRuleRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ChapterRule>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt
            FROM ChapterRules
            ORDER BY SortOrder, Name;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ChapterRule>();

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ChapterRule(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt64(4) == 1,
                reader.GetString(5),
                reader.GetString(6)));
        }

        return items;
    }

    public async Task<IReadOnlyList<ChapterRule>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        var rules = await GetAllAsync(cancellationToken);
        return rules.Where(rule => rule.IsEnabled).OrderBy(rule => rule.SortOrder).ToArray();
    }

    public async Task SaveAsync(ChapterRule rule, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ChapterRules (Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt)
            VALUES ($id, $name, $pattern, $sortOrder, $isEnabled, $createdAt, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Pattern = excluded.Pattern,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedAt = excluded.UpdatedAt;
            """;

        command.Parameters.AddWithValue("$id", rule.Id);
        command.Parameters.AddWithValue("$name", rule.Name);
        command.Parameters.AddWithValue("$pattern", rule.Pattern);
        command.Parameters.AddWithValue("$sortOrder", rule.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", rule.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", rule.CreatedAt);
        command.Parameters.AddWithValue("$updatedAt", rule.UpdatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ChapterRules WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", ruleId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MoveAsync(string ruleId, int newSortOrder, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ChapterRules
            SET SortOrder = $sortOrder,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$id", ruleId);
        command.Parameters.AddWithValue("$sortOrder", newSortOrder);
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var item in order)
        {
            var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                """
                UPDATE ChapterRules
                SET SortOrder = $sortOrder,
                    UpdatedAt = $updatedAt
                WHERE Id = $id;
                """;

            command.Parameters.AddWithValue("$id", item.RuleId);
            command.Parameters.AddWithValue("$sortOrder", item.SortOrder);
            command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> ImportDefaultsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await BackfillBuiltInIdsAsync(connection, cancellationToken);

        var changed = 0;

        foreach (var definition in DefaultChapterRules.All)
        {
            var selectCommand = connection.CreateCommand();
            selectCommand.CommandText =
                """
                SELECT Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt
                FROM ChapterRules
                WHERE Id = $id
                LIMIT 1;
                """;
            selectCommand.Parameters.AddWithValue("$id", definition.Id);

            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var currentName = reader.GetString(1);
                var currentPattern = reader.GetString(2);

                if (string.Equals(currentName, definition.Name, StringComparison.Ordinal) &&
                    string.Equals(currentPattern, definition.Pattern, StringComparison.Ordinal))
                {
                    continue;
                }

                var updateCommand = connection.CreateCommand();
                updateCommand.CommandText =
                    """
                    UPDATE ChapterRules
                    SET Name = $name,
                        Pattern = $pattern,
                        UpdatedAt = $updatedAt
                    WHERE Id = $id;
                    """;
                updateCommand.Parameters.AddWithValue("$id", definition.Id);
                updateCommand.Parameters.AddWithValue("$name", definition.Name);
                updateCommand.Parameters.AddWithValue("$pattern", definition.Pattern);
                updateCommand.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
                changed++;
                continue;
            }

            var utcNow = DateTime.UtcNow.ToString("O");
            var insertCommand = connection.CreateCommand();
            insertCommand.CommandText =
                """
                INSERT INTO ChapterRules (Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt)
                VALUES ($id, $name, $pattern, $sortOrder, $isEnabled, $createdAt, $updatedAt);
                """;
            insertCommand.Parameters.AddWithValue("$id", definition.Id);
            insertCommand.Parameters.AddWithValue("$name", definition.Name);
            insertCommand.Parameters.AddWithValue("$pattern", definition.Pattern);
            insertCommand.Parameters.AddWithValue("$sortOrder", definition.SortOrder);
            insertCommand.Parameters.AddWithValue("$isEnabled", 1);
            insertCommand.Parameters.AddWithValue("$createdAt", utcNow);
            insertCommand.Parameters.AddWithValue("$updatedAt", utcNow);

            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            changed++;
        }

        return changed;
    }

    private static async Task BackfillBuiltInIdsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var definition in DefaultChapterRules.All)
        {
            var builtInExistsCommand = connection.CreateCommand();
            builtInExistsCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM ChapterRules
                WHERE Id = $id;
                """;
            builtInExistsCommand.Parameters.AddWithValue("$id", definition.Id);
            var builtInExists = Convert.ToInt32(await builtInExistsCommand.ExecuteScalarAsync(cancellationToken)) > 0;
            if (builtInExists)
            {
                continue;
            }

            var findMatchCommand = connection.CreateCommand();
            findMatchCommand.CommandText =
                """
                SELECT Id
                FROM ChapterRules
                WHERE Name = $name AND Pattern = $pattern
                ORDER BY CreatedAt, Id
                LIMIT 1;
                """;
            findMatchCommand.Parameters.AddWithValue("$name", definition.Name);
            findMatchCommand.Parameters.AddWithValue("$pattern", definition.Pattern);

            var matchId = (string?)await findMatchCommand.ExecuteScalarAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(matchId) ||
                string.Equals(matchId, definition.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var updateCommand = connection.CreateCommand();
            updateCommand.CommandText =
                """
                UPDATE ChapterRules
                SET Id = $newId,
                    UpdatedAt = $updatedAt
                WHERE Id = $oldId;
                """;
            updateCommand.Parameters.AddWithValue("$newId", definition.Id);
            updateCommand.Parameters.AddWithValue("$oldId", matchId);
            updateCommand.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
