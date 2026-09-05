using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.App.Features.Books.Shared;

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

    public static EffectiveReadingProgress Project(
        string bookId,
        IReadOnlyList<BookChapterSummary> catalog,
        BookReadingPosition? persisted,
        PlaybackSnapshot snapshot,
        Func<int, int?>? chapterPositionResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);

        chapterPositionResolver ??= chapterIndex => FindChapterPosition(catalog, chapterIndex);
        var totalChapterCount = catalog.Count;
        var persistedChapterPosition = persisted?.ChapterIndex is int persistedChapterIndex
            ? chapterPositionResolver(persistedChapterIndex)
            : null;
        var currentChapterIndex = persistedChapterPosition is int
            ? persisted!.ChapterIndex
            : (int?)null;
        var baselineTitle = persistedChapterPosition is int baselinePosition
            ? catalog[baselinePosition].Title
            : "未开始";

        var baseline = new EffectiveReadingProgress(
            currentChapterIndex,
            baselineTitle,
            persistedChapterPosition is int position ? totalChapterCount - position - 1 : totalChapterCount,
            persistedChapterPosition is not null && totalChapterCount > 0
                ? (double)(persistedChapterPosition.Value + 1) / totalChapterCount
                : 0,
            currentChapterIndex is not null);

        if (!string.Equals(snapshot.BookId, bookId, StringComparison.Ordinal))
        {
            return baseline;
        }

        if (totalChapterCount <= 0)
        {
            return new EffectiveReadingProgress(
                null,
                snapshot.ChapterTitle ?? baselineTitle,
                0,
                0,
                false);
        }

        var snapshotChapterPosition = chapterPositionResolver(snapshot.ChapterIndex);
        if (snapshotChapterPosition is not int activePosition)
        {
            return baseline;
        }

        var activeChapter = catalog[activePosition];
        return new EffectiveReadingProgress(
            activeChapter.ChapterIndex,
            snapshot.ChapterTitle ?? activeChapter.Title,
            totalChapterCount - activePosition - 1,
            (double)(activePosition + 1) / totalChapterCount,
            true);
    }

    private static int? FindChapterPosition(
        IReadOnlyList<BookChapterSummary> catalog,
        int? chapterIndex)
    {
        if (chapterIndex is not int targetChapterIndex)
        {
            return null;
        }

        for (var position = 0; position < catalog.Count; position++)
        {
            if (catalog[position].ChapterIndex == targetChapterIndex)
            {
                return position;
            }
        }

        return null;
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
