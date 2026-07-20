namespace NovelSpeaker.Application.Playback;

/// <summary>
/// A position selected by the playback state machine.
/// </summary>
internal readonly record struct PlaybackPosition(int ChapterIndex, int SegmentIndex);

/// <summary>
/// A restored position, including the audio offset that is safe to resume.
/// </summary>
internal readonly record struct PlaybackRestoredPosition(
    int ChapterIndex,
    int SegmentIndex,
    long ResumePositionMilliseconds);

/// <summary>
/// Pure position calculations for already available playback content.
/// Chapter loading remains owned by <see cref="PlaybackCoordinator"/>.
/// </summary>
internal static class PlaybackPositionResolver
{
    internal static IReadOnlyList<int> GetChapterSearchOrder(
        IReadOnlyList<PlaybackChapterContent> chapters,
        int? preferredChapterIndex,
        int searchDirection)
    {
        ArgumentNullException.ThrowIfNull(chapters);

        var orderedChapters = chapters
            .OrderBy(chapter => chapter.ChapterIndex)
            .ToArray();
        if (orderedChapters.Length == 0)
        {
            return Array.Empty<int>();
        }

        var startIndex = ResolveChapterSearchStartIndex(
            orderedChapters,
            preferredChapterIndex,
            searchDirection);
        if (startIndex < 0)
        {
            return Array.Empty<int>();
        }

        var step = searchDirection < 0 ? -1 : 1;
        var result = new List<int>(orderedChapters.Length - startIndex);
        for (var index = startIndex;
             index >= 0 && index < orderedChapters.Length;
             index += step)
        {
            result.Add(orderedChapters[index].ChapterIndex);
        }

        return result;
    }

    /// <summary>
    /// Resolves one position in a chapter without loading it or changing any state.
    /// Empty chapters and segments without speech are not playable positions.
    /// </summary>
    internal static PlaybackPosition? ResolvePlayablePositionInChapter(
        PlaybackChapterContent chapter,
        int? preferredChapterIndex,
        int? preferredSegmentIndex,
        int searchDirection,
        bool preferLastSegmentWhenSearchingBackward)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        if (chapter.LoadState != PlaybackChapterLoadState.Loaded || chapter.Segments.Count == 0)
        {
            return null;
        }

        var startIndex = ResolveSegmentSearchStartIndex(
            chapter,
            preferredChapterIndex,
            preferredSegmentIndex,
            searchDirection,
            preferLastSegmentWhenSearchingBackward);
        var step = searchDirection < 0 ? -1 : 1;
        for (var index = startIndex;
             index >= 0 && index < chapter.Segments.Count;
             index += step)
        {
            if (!string.IsNullOrWhiteSpace(chapter.Segments[index].SpeechText))
            {
                return new PlaybackPosition(chapter.ChapterIndex, index);
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the next or previous playable segment in the current chapter.
    /// The caller can then search adjacent chapters if this returns null.
    /// </summary>
    internal static PlaybackPosition? ResolveRelativeSegmentInChapter(
        PlaybackChapterContent chapter,
        int segmentIndex,
        int delta)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        if (delta == 0)
        {
            return segmentIndex >= 0 &&
                   segmentIndex < chapter.Segments.Count &&
                   chapter.LoadState == PlaybackChapterLoadState.Loaded
                ? new PlaybackPosition(chapter.ChapterIndex, segmentIndex)
                : null;
        }

        if (chapter.LoadState != PlaybackChapterLoadState.Loaded)
        {
            return null;
        }

        var step = delta < 0 ? -1 : 1;
        var index = segmentIndex + delta;
        while (index >= 0 && index < chapter.Segments.Count)
        {
            if (!string.IsNullOrWhiteSpace(chapter.Segments[index].SpeechText))
            {
                return new PlaybackPosition(chapter.ChapterIndex, index);
            }

            index += step;
        }

        return null;
    }

    /// <summary>
    /// Maps persisted raw-character progress to the first playable segment at or
    /// after that offset, falling back to the last playable segment.
    /// </summary>
    internal static int FindMappedSegmentIndex(
        PlaybackChapterContent chapter,
        int characterOffset)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        for (var index = 0; index < chapter.Segments.Count; index++)
        {
            if (chapter.Segments[index].StartOffset >= characterOffset &&
                !string.IsNullOrWhiteSpace(chapter.Segments[index].SpeechText))
            {
                return index;
            }
        }

        for (var index = chapter.Segments.Count - 1; index >= 0; index--)
        {
            if (!string.IsNullOrWhiteSpace(chapter.Segments[index].SpeechText))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Restores a position only when the requested chapter is already loaded.
    /// Loading and fallback across chapters are deliberately left to the coordinator.
    /// </summary>
    internal static PlaybackRestoredPosition? ResolveRestoredPosition(
        PlaybackBookContent book,
        ReadingProgressEntry progress)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(progress);

        var chapter = book.Chapters.FirstOrDefault(
            candidate => candidate.ChapterIndex == progress.ChapterIndex);
        if (chapter is null || chapter.LoadState != PlaybackChapterLoadState.Loaded)
        {
            return null;
        }

        var mappedSegmentIndex = FindMappedSegmentIndex(chapter, progress.CharacterOffset);
        if (mappedSegmentIndex < 0)
        {
            return null;
        }

        var canResumeAudioPosition = progress.SegmentIndex >= 0 &&
                                     progress.SegmentIndex < chapter.Segments.Count &&
                                     mappedSegmentIndex == progress.SegmentIndex &&
                                     chapter.Segments[progress.SegmentIndex].StartOffset == progress.CharacterOffset;
        return new PlaybackRestoredPosition(
            chapter.ChapterIndex,
            mappedSegmentIndex,
            canResumeAudioPosition ? progress.AudioPositionMilliseconds : 0);
    }

    internal static int? FindAdjacentChapterIndex(
        IReadOnlyList<PlaybackChapterContent> chapters,
        int chapterIndex,
        int delta)
    {
        ArgumentNullException.ThrowIfNull(chapters);

        var orderedChapters = chapters
            .OrderBy(chapter => chapter.ChapterIndex)
            .ToArray();
        if (orderedChapters.Length == 0)
        {
            return null;
        }

        var currentIndex = Array.FindIndex(
            orderedChapters,
            chapter => chapter.ChapterIndex == chapterIndex);
        if (currentIndex < 0)
        {
            return delta < 0
                ? orderedChapters[^1].ChapterIndex
                : orderedChapters[0].ChapterIndex;
        }

        var targetIndex = currentIndex + (delta < 0 ? -1 : 1);
        return targetIndex < 0 || targetIndex >= orderedChapters.Length
            ? null
            : orderedChapters[targetIndex].ChapterIndex;
    }

    internal static bool HasNextSegment(
        PlaybackBookContent book,
        int chapterIndex,
        int segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(book);

        var chapter = book.Chapters.FirstOrDefault(
            candidate => candidate.ChapterIndex == chapterIndex);
        if (chapter is not null &&
            ResolveRelativeSegmentInChapter(chapter, segmentIndex, 1) is not null)
        {
            return true;
        }

        return FindAdjacentChapterIndex(book.Chapters, chapterIndex, 1) is not null;
    }

    private static int ResolveChapterSearchStartIndex(
        IReadOnlyList<PlaybackChapterContent> chapters,
        int? preferredChapterIndex,
        int searchDirection)
    {
        if (preferredChapterIndex is null)
        {
            return searchDirection < 0 ? chapters.Count - 1 : 0;
        }

        if (searchDirection >= 0)
        {
            for (var index = 0; index < chapters.Count; index++)
            {
                if (chapters[index].ChapterIndex >= preferredChapterIndex.Value)
                {
                    return index;
                }
            }

            return -1;
        }

        for (var index = chapters.Count - 1; index >= 0; index--)
        {
            if (chapters[index].ChapterIndex <= preferredChapterIndex.Value)
            {
                return index;
            }
        }

        return -1;
    }

    private static int ResolveSegmentSearchStartIndex(
        PlaybackChapterContent chapter,
        int? preferredChapterIndex,
        int? preferredSegmentIndex,
        int searchDirection,
        bool preferLastSegmentWhenSearchingBackward)
    {
        if (preferredChapterIndex == chapter.ChapterIndex &&
            preferredSegmentIndex is >= 0)
        {
            return Math.Min(preferredSegmentIndex.Value, chapter.Segments.Count - 1);
        }

        return searchDirection < 0 && preferLastSegmentWhenSearchingBackward
            ? chapter.Segments.Count - 1
            : 0;
    }
}
