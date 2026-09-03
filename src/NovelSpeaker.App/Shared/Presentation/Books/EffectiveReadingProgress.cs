using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.App.Shared.Presentation.Books;

/// <summary>
/// The read-only reading progress presented by a page after applying the live playback snapshot.
/// </summary>
public sealed record EffectiveReadingProgress(
    int? CurrentChapterIndex,
    string CurrentChapterTitle,
    int RemainingChapterCount,
    double OverallProgress,
    bool HasReadingProgress);

/// <summary>
/// Merges persisted book projections with the immutable playback snapshot without owning state.
/// </summary>
public static class EffectiveReadingProgressProjector
{
    public static EffectiveReadingProgress Project(BookSummary persisted, PlaybackSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        ArgumentNullException.ThrowIfNull(snapshot);

        var baseline = new EffectiveReadingProgress(
            persisted.CurrentChapterIndex,
            persisted.CurrentChapterTitle,
            persisted.RemainingChapterCount,
            persisted.OverallProgress,
            persisted.HasReadingProgress);
        return Project(
            persisted.Id,
            persisted.TotalChapterCount,
            baseline,
            snapshot,
            snapshot.ChapterTitle ?? persisted.CurrentChapterTitle);
    }

    public static EffectiveReadingProgress Project(BookDetails persisted, PlaybackSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        ArgumentNullException.ThrowIfNull(snapshot);

        var baselineTitle = persisted.Chapters.FirstOrDefault(chapter => chapter.IsCurrent)?.Title;
        if (string.IsNullOrWhiteSpace(baselineTitle))
        {
            baselineTitle = persisted.CurrentChapterIndex is int currentChapterIndex
                ? $"第 {currentChapterIndex + 1} 章"
                : "未开始";
        }

        var baseline = new EffectiveReadingProgress(
            persisted.CurrentChapterIndex,
            baselineTitle,
            persisted.RemainingChapterCount,
            persisted.OverallProgress,
            persisted.HasReadingProgress);
        var snapshotTitle = snapshot.ChapterTitle ?? persisted.Chapters
            .FirstOrDefault(chapter => chapter.ChapterIndex == snapshot.ChapterIndex)?.Title;
        return Project(
            persisted.Id,
            persisted.TotalChapterCount,
            baseline,
            snapshot,
            snapshotTitle ?? baselineTitle);
    }

    private static EffectiveReadingProgress Project(
        string targetBookId,
        int totalChapterCount,
        EffectiveReadingProgress baseline,
        PlaybackSnapshot snapshot,
        string snapshotFallbackTitle)
    {
        if (!string.Equals(snapshot.BookId, targetBookId, StringComparison.Ordinal))
        {
            return baseline;
        }

        if (totalChapterCount <= 0)
        {
            return new EffectiveReadingProgress(
                null,
                snapshotFallbackTitle,
                0,
                0,
                false);
        }

        var chapterIndex = Math.Clamp(snapshot.ChapterIndex, 0, totalChapterCount - 1);
        return new EffectiveReadingProgress(
            chapterIndex,
            snapshotFallbackTitle,
            Math.Max(0, totalChapterCount - chapterIndex - 1),
            (double)(chapterIndex + 1) / totalChapterCount,
            true);
    }
}
