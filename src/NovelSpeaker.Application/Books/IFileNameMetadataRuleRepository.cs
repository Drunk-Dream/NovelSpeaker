using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

public interface IFileNameMetadataRuleRepository
{
    Task<IReadOnlyList<FileNameMetadataRule>> GetAllAsync(CancellationToken cancellationToken);

    Task SaveAsync(FileNameMetadataRule rule, CancellationToken cancellationToken);

    Task DeleteAsync(string ruleId, CancellationToken cancellationToken);

    Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken);
}
