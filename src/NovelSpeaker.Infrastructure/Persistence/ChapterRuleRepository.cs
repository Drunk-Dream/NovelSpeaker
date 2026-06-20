using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books;

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

    public async Task<int> ImportDefaultsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var inserted = 0;

        foreach (var (name, pattern) in DefaultChapterRules.All)
        {
            var existsCommand = connection.CreateCommand();
            existsCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM ChapterRules
                WHERE Name = $name AND Pattern = $pattern;
                """;
            existsCommand.Parameters.AddWithValue("$name", name);
            existsCommand.Parameters.AddWithValue("$pattern", pattern);

            var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken)) > 0;
            if (exists)
            {
                continue;
            }

            var utcNow = DateTime.UtcNow.ToString("O");
            var insertCommand = connection.CreateCommand();
            insertCommand.CommandText =
                """
                INSERT INTO ChapterRules (Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt)
                VALUES ($id, $name, $pattern, $sortOrder, $isEnabled, $createdAt, $updatedAt);
                """;
            insertCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insertCommand.Parameters.AddWithValue("$name", name);
            insertCommand.Parameters.AddWithValue("$pattern", pattern);
            insertCommand.Parameters.AddWithValue("$sortOrder", inserted * 10);
            insertCommand.Parameters.AddWithValue("$isEnabled", 1);
            insertCommand.Parameters.AddWithValue("$createdAt", utcNow);
            insertCommand.Parameters.AddWithValue("$updatedAt", utcNow);

            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            inserted++;
        }

        return inserted;
    }
}
