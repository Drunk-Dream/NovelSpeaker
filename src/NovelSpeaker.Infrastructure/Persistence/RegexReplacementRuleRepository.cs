using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>Stores valid global regex replacement rule rows while isolating malformed history.</summary>
public sealed class RegexReplacementRuleRepository : IRegexReplacementRuleRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public RegexReplacementRuleRepository(
        ISqliteConnectionFactory connectionFactory,
        TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, IsEnabled, SortOrder, Pattern, Replacement, Scope, CreatedAt, UpdatedAt
            FROM RegexReplacementRules
            ORDER BY SortOrder, Id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rules = new List<RegexReplacementRule>();

        while (await reader.ReadAsync(cancellationToken))
        {
            if (TryMap(reader, out var rule))
            {
                rules.Add(rule);
            }
        }

        return rules;
    }

    public async Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO RegexReplacementRules
                (Id, Name, IsEnabled, SortOrder, Pattern, Replacement, Scope, CreatedAt, UpdatedAt)
            VALUES
                ($id, $name, $enabled, $sort, $pattern, $replacement, $scope, $created, $updated)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Pattern = excluded.Pattern,
                Replacement = excluded.Replacement,
                Scope = excluded.Scope,
                UpdatedAt = excluded.UpdatedAt;
            """;
        Bind(command, rule);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateEnabledAsync(
        Guid ruleId,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE RegexReplacementRules
            SET IsEnabled = $enabled,
                UpdatedAt = $updated
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$enabled", isEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$updated", SqliteDateTimeMapper.Format(_timeProvider.GetUtcNow()));
        command.Parameters.AddWithValue("$id", ruleId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveOrderAsync(
        IReadOnlyList<(Guid RuleId, int SortOrder)> order,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var updatedAt = SqliteDateTimeMapper.Format(_timeProvider.GetUtcNow());

        foreach (var (ruleId, sortOrder) in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                """
                UPDATE RegexReplacementRules
                SET SortOrder = $sort,
                    UpdatedAt = $updated
                WHERE Id = $id;
                """;
            command.Parameters.AddWithValue("$sort", sortOrder);
            command.Parameters.AddWithValue("$updated", updatedAt);
            command.Parameters.AddWithValue("$id", ruleId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RegexReplacementRules WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", ruleId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool TryMap(SqliteDataReader reader, out RegexReplacementRule rule)
    {
        rule = null!;
        try
        {
            if (!Guid.TryParse(reader.GetString(0), out var id) ||
                !TryParseScope(reader.GetString(6), out var scope) ||
                !DateTimeOffset.TryParse(reader.GetString(7), out var createdAt) ||
                !DateTimeOffset.TryParse(reader.GetString(8), out var updatedAt))
            {
                return false;
            }

            rule = new RegexReplacementRule(
                id,
                reader.GetString(1),
                reader.GetInt64(2) != 0,
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                scope,
                createdAt,
                updatedAt);
            return true;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryParseScope(string value, out RegexReplacementScope scope)
    {
        return Enum.TryParse(value, true, out scope) &&
               Enum.IsDefined(scope) &&
               !int.TryParse(value, out _);
    }

    private static void Bind(SqliteCommand command, RegexReplacementRule rule)
    {
        command.Parameters.AddWithValue("$id", rule.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", rule.Name);
        command.Parameters.AddWithValue("$enabled", rule.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$sort", rule.SortOrder);
        command.Parameters.AddWithValue("$pattern", rule.Pattern);
        command.Parameters.AddWithValue("$replacement", rule.Replacement);
        command.Parameters.AddWithValue("$scope", rule.Scope.ToString());
        command.Parameters.AddWithValue("$created", SqliteDateTimeMapper.Format(rule.CreatedAt));
        command.Parameters.AddWithValue("$updated", SqliteDateTimeMapper.Format(rule.UpdatedAt));
    }
}
