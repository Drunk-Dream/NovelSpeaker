namespace NovelSpeaker.Application.Books;

/// <summary>
/// Owns previews and application of built-in chapter-rule default operations.
/// </summary>
public interface IChapterRuleManagementService
{
    Task<ChapterRuleDefaultsPreview> PreviewDefaultsAsync(
        ChapterRuleDefaultsMode mode,
        CancellationToken cancellationToken);

    Task<ChapterRuleDefaultsApplyResult> ApplyDefaultsAsync(
        ChapterRuleDefaultsMode mode,
        CancellationToken cancellationToken);
}
