namespace NovelSpeaker.Application.Books;

/// <summary>Owns validation, field-level saves, and stable ordering for regex replacement rules.</summary>
public interface IRegexReplacementRuleWorkspaceService
{
    Task<IReadOnlyList<RegexReplacementRuleListItem>> GetRulesAsync(CancellationToken cancellationToken);
    Task<RegexReplacementRuleEditorModel?> GetEditorAsync(Guid ruleId, CancellationToken cancellationToken);
    Task<RegexReplacementRuleEditorModel> SaveEditorAsync(RegexReplacementRuleEditorModel editor, CancellationToken cancellationToken);
    Task SetRuleEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken);
    Task<string?> ExportRuleJsonAsync(Guid ruleId, CancellationToken cancellationToken);
    Task<RuleJsonImportResult> ImportJsonAsync(string json, CancellationToken cancellationToken);
    Task SaveOrderAsync(IReadOnlyList<Guid> orderedRuleIds, CancellationToken cancellationToken);
    Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken);
}
