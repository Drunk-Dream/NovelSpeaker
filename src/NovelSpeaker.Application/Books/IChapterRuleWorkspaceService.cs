namespace NovelSpeaker.Application.Books;

/// <summary>
/// Owns chapter-rule workspace behaviors for list projection, editor persistence, and ordering.
/// </summary>
public interface IChapterRuleWorkspaceService
{
    Task<IReadOnlyList<ChapterRuleListItem>> GetRulesAsync(CancellationToken cancellationToken);

    Task<ChapterRuleEditorModel?> GetEditorAsync(string ruleId, CancellationToken cancellationToken);

    Task<ChapterRuleEditorModel> SaveEditorAsync(ChapterRuleEditorModel editor, CancellationToken cancellationToken);

    Task DeleteRuleAsync(string ruleId, CancellationToken cancellationToken);

    Task SetRuleEnabledAsync(string ruleId, bool isEnabled, CancellationToken cancellationToken);

    Task SaveOrderAsync(IReadOnlyList<string> orderedRuleIds, CancellationToken cancellationToken);

    Task<ChapterRuleDefaultsApplyResult> ApplyDefaultsAsync(
        ChapterRuleDefaultsMode mode,
        CancellationToken cancellationToken);
}
