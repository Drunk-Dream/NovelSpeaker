namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents the preview of applying one built-in chapter-rule defaults operation.
/// </summary>
public sealed record ChapterRuleDefaultsPreview(
    ChapterRuleDefaultsMode Mode,
    IReadOnlyList<ChapterRuleChangeSummary> Changes)
{
    public int AddedCount => Changes.Count(change => change.ChangeKind == ChapterRuleChangeKind.Added);

    public int UpdatedCount => Changes.Count(change => change.ChangeKind == ChapterRuleChangeKind.Updated);

    public int UnchangedCount => Changes.Count(change => change.ChangeKind == ChapterRuleChangeKind.Unchanged);
}
