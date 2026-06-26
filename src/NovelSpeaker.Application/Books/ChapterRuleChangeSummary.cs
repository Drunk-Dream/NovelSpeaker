namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents one built-in chapter-rule change in a defaults preview.
/// </summary>
public sealed record ChapterRuleChangeSummary(
    string RuleId,
    string Name,
    string Pattern,
    int SortOrder,
    ChapterRuleChangeKind ChangeKind);
