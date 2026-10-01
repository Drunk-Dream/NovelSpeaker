using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

public sealed class FileNameMetadataRuleRepository(ISqliteConnectionFactory connections) : IFileNameMetadataRuleRepository
{
    private readonly MetadataRuleTable _table = new(connections, "FileNameMetadataRules");

    public async Task<IReadOnlyList<FileNameMetadataRule>> GetAllAsync(CancellationToken cancellationToken) =>
        (await _table.GetAllAsync(cancellationToken)).Select(row =>
            new FileNameMetadataRule(row.Id, row.Name, row.Pattern, row.SortOrder, row.IsEnabled, row.CreatedAt, row.UpdatedAt)).ToArray();

    public Task SaveAsync(FileNameMetadataRule rule, CancellationToken cancellationToken) =>
        _table.SaveAsync(new MetadataRuleRow(rule.Id, rule.Name, rule.Pattern, rule.SortOrder, rule.IsEnabled, rule.CreatedAt, rule.UpdatedAt), cancellationToken);

    public Task DeleteAsync(string ruleId, CancellationToken cancellationToken) => _table.DeleteAsync(ruleId, cancellationToken);

    public Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken) =>
        _table.SaveOrderAsync(order, cancellationToken);
}

public sealed class TextHeaderMetadataRuleRepository(ISqliteConnectionFactory connections) : ITextHeaderMetadataRuleRepository
{
    private readonly MetadataRuleTable _table = new(connections, "TextHeaderMetadataRules");

    public async Task<IReadOnlyList<TextHeaderMetadataRule>> GetAllAsync(CancellationToken cancellationToken) =>
        (await _table.GetAllAsync(cancellationToken)).Select(row =>
            new TextHeaderMetadataRule(row.Id, row.Name, row.Pattern, row.SortOrder, row.IsEnabled, row.CreatedAt, row.UpdatedAt)).ToArray();

    public Task SaveAsync(TextHeaderMetadataRule rule, CancellationToken cancellationToken) =>
        _table.SaveAsync(new MetadataRuleRow(rule.Id, rule.Name, rule.Pattern, rule.SortOrder, rule.IsEnabled, rule.CreatedAt, rule.UpdatedAt), cancellationToken);

    public Task DeleteAsync(string ruleId, CancellationToken cancellationToken) => _table.DeleteAsync(ruleId, cancellationToken);

    public Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken) =>
        _table.SaveOrderAsync(order, cancellationToken);
}

internal sealed record MetadataRuleRow(
    string Id,
    string Name,
    string Pattern,
    int SortOrder,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed class MetadataRuleTable(ISqliteConnectionFactory connections, string tableName)
{
    public async Task<IReadOnlyList<MetadataRuleRow>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt FROM {tableName} ORDER BY SortOrder, Id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<MetadataRuleRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (SqliteDateTimeMapper.TryParse(reader.GetString(5), out var createdAt) &&
                SqliteDateTimeMapper.TryParse(reader.GetString(6), out var updatedAt))
            {
                rows.Add(new MetadataRuleRow(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                    reader.GetInt64(4) == 1, createdAt, updatedAt));
            }
        }

        return rows;
    }

    public async Task SaveAsync(MetadataRuleRow row, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {tableName} (Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt)
            VALUES ($id, $name, $pattern, $sortOrder, $isEnabled, $createdAt, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Pattern = excluded.Pattern,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedAt = excluded.UpdatedAt;
            """;
        AddRowParameters(command, row);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {tableName} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", ruleId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var item in order)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"UPDATE {tableName} SET SortOrder = $sortOrder WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", item.RuleId);
            command.Parameters.AddWithValue("$sortOrder", item.SortOrder);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static void AddRowParameters(SqliteCommand command, MetadataRuleRow row)
    {
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$name", row.Name);
        command.Parameters.AddWithValue("$pattern", row.Pattern);
        command.Parameters.AddWithValue("$sortOrder", row.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", row.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", SqliteDateTimeMapper.Format(row.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqliteDateTimeMapper.Format(row.UpdatedAt));
    }
}
