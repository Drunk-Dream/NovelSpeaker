using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>SQLite storage for global regex replacement rules.</summary>
public sealed class RegexReplacementRuleRepository : IRegexReplacementRuleRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public RegexReplacementRuleRepository(ISqliteConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, IsEnabled, SortOrder, Pattern, Replacement, Scope, CreatedAt, UpdatedAt FROM RegexReplacementRules ORDER BY SortOrder, Id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rules = new List<RegexReplacementRule>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Guid.TryParse(reader.GetString(0), out var id) || !Enum.TryParse<RegexReplacementScope>(reader.GetString(6), true, out var scope))
            {
                continue;
            }

            rules.Add(new RegexReplacementRule(id, reader.GetString(1), reader.GetInt64(2) != 0, reader.GetInt32(3), reader.GetString(4), reader.GetString(5), scope, DateTimeOffset.Parse(reader.GetString(7)), DateTimeOffset.Parse(reader.GetString(8))));
        }
        return rules;
    }

    public async Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RegexReplacementRules (Id, Name, IsEnabled, SortOrder, Pattern, Replacement, Scope, CreatedAt, UpdatedAt)
            VALUES ($id, $name, $enabled, $sort, $pattern, $replacement, $scope, $created, $updated)
            ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name, Pattern = excluded.Pattern, Replacement = excluded.Replacement, Scope = excluded.Scope, UpdatedAt = excluded.UpdatedAt;
            """;
        Bind(command, rule);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken)
    {
        await UpdateSingleAsync("IsEnabled = $value", ruleId, isEnabled ? 1 : 0, cancellationToken);
    }

    public async Task SaveOrderAsync(IReadOnlyList<(Guid RuleId, int SortOrder)> order, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var (ruleId, sortOrder) in order)
        {
            var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "UPDATE RegexReplacementRules SET SortOrder = $sort, UpdatedAt = $updated WHERE Id = $id;";
            command.Parameters.AddWithValue("$sort", sortOrder); command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$id", ruleId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "DELETE FROM RegexReplacementRules WHERE Id = $id;"; command.Parameters.AddWithValue("$id", ruleId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateSingleAsync(string assignment, Guid id, int value, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = $"UPDATE RegexReplacementRules SET {assignment}, UpdatedAt = $updated WHERE Id = $id;";
        command.Parameters.AddWithValue("$value", value); command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Bind(SqliteCommand command, RegexReplacementRule rule)
    {
        command.Parameters.AddWithValue("$id", rule.Id.ToString("D")); command.Parameters.AddWithValue("$name", rule.Name); command.Parameters.AddWithValue("$enabled", rule.IsEnabled ? 1 : 0); command.Parameters.AddWithValue("$sort", rule.SortOrder); command.Parameters.AddWithValue("$pattern", rule.Pattern); command.Parameters.AddWithValue("$replacement", rule.Replacement); command.Parameters.AddWithValue("$scope", rule.Scope.ToString()); command.Parameters.AddWithValue("$created", rule.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$updated", rule.UpdatedAt.ToString("O"));
    }
}
