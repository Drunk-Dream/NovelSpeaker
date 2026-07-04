using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Owns persistence and ordering of chapter-detection rules.
/// </summary>
public interface IChapterRuleRepository
{
    Task<IReadOnlyList<ChapterRule>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ChapterRule>> GetEnabledAsync(CancellationToken cancellationToken);
    Task SaveAsync(ChapterRule rule, CancellationToken cancellationToken);
    Task DeleteAsync(string ruleId, CancellationToken cancellationToken);
    Task MoveAsync(string ruleId, int newSortOrder, CancellationToken cancellationToken);
    Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken);
    Task<int> ImportDefaultsAsync(CancellationToken cancellationToken);
}
