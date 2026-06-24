namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents the editable subset of a chapter rule used by the UI.
/// </summary>
public sealed record ChapterRuleDraft(
    string Id,
    string Name,
    string Pattern,
    int SortOrder,
    bool IsEnabled);
