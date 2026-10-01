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
