namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents the editable chapter-rule fields consumed by the workspace editor.
/// </summary>
public sealed record ChapterRuleEditorModel(
    string? Id,
    string Name,
    string Pattern,
    bool IsEnabled,
    bool IsBuiltIn,
    bool CanDelete);
