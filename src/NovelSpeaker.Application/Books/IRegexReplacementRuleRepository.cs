using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>Persists global regex replacement rules without coupling callers to SQLite.</summary>
public interface IRegexReplacementRuleRepository
{
    Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken);
    Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken);
    Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken);
    Task SaveOrderAsync(IReadOnlyList<(Guid RuleId, int SortOrder)> order, CancellationToken cancellationToken);
    Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken);
}
