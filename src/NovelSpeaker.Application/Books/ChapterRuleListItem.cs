namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents one ordered chapter rule row projected for the workspace list.
/// </summary>
public sealed record ChapterRuleListItem(
    string Id,
    string Name,
    string PatternSummary,
    bool IsEnabled,
    int SortOrder,
    bool IsBuiltIn);
