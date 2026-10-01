using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

public interface ITextHeaderMetadataRuleRepository
{
    Task<IReadOnlyList<TextHeaderMetadataRule>> GetAllAsync(CancellationToken cancellationToken);

    Task SaveAsync(TextHeaderMetadataRule rule, CancellationToken cancellationToken);

    Task DeleteAsync(string ruleId, CancellationToken cancellationToken);

    Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken);
}
