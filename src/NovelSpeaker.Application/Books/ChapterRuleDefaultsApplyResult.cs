namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents the outcome of applying one built-in chapter-rule defaults operation.
/// </summary>
public sealed record ChapterRuleDefaultsApplyResult(
    ChapterRuleDefaultsMode Mode,
    int AddedCount,
    int UpdatedCount,
    int UnchangedCount);
