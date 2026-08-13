using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackPositionResolverTests
{
    [Fact]
    public void GetChapterSearchOrder_handles_first_and_last_boundaries()
    {
        var chapters = new[]
        {
            PlaybackChapterContent.FromLoaded(0, "第一章", [Speech(0, 0, "一")]),
            PlaybackChapterContent.FromLoaded(2, "第三章", [Speech(0, 10, "三")]),
            PlaybackChapterContent.FromLoaded(5, "第六章", [Speech(0, 20, "六")])
        };

        foreach (var (preferredChapterIndex, direction, expectedChapterIndexes) in new[]
                 {
                     ((int?)null, 1, new[] { 0, 2, 5 }),
                     ((int?)null, -1, new[] { 5, 2, 0 }),
                     ((int?)0, -1, new[] { 0 }),
                     ((int?)5, 1, new[] { 5 }),
                     ((int?)3, 1, new[] { 5 }),
                     ((int?)3, -1, new[] { 2, 0 }),
                     ((int?)-1, 1, new[] { 0, 2, 5 }),
                     ((int?)99, -1, new[] { 5, 2, 0 })
                 })
        {
            var result = PlaybackPositionResolver.GetChapterSearchOrder(
                chapters,
                preferredChapterIndex,
                direction);

            Assert.Equal(expectedChapterIndexes, result);
        }
    }

    [Fact]
    public void ResolvePlayablePositionInChapter_skips_empty_chapters_and_continuous_empty_speech()
    {
        var emptyChapter = PlaybackChapterContent.FromLoaded(0, "空章", []);
        var chapter = PlaybackChapterContent.FromLoaded(
            1,
            "连续空语音",
            [
                Speech(0, 0, "", ""),
                Speech(1, 4, " ", "仅展示"),
                Speech(2, 8, "可播放", "可播放")
            ]);

        Assert.Null(PlaybackPositionResolver.ResolvePlayablePositionInChapter(
            emptyChapter,
            null,
            null,
            1,
            false));

        Assert.Equal(
            new PlaybackPosition(1, 2),
            PlaybackPositionResolver.ResolvePlayablePositionInChapter(
                chapter,
                1,
                0,
                1,
                false));
    }

    [Fact]
    public void FindAdjacentChapterIndex_observes_book_boundaries()
    {
        var chapters = new[]
        {
            PlaybackChapterContent.FromLoaded(0, "第一章", [Speech(0, 0, "一")]),
            PlaybackChapterContent.FromLoaded(2, "第三章", [Speech(0, 10, "三")]),
            PlaybackChapterContent.FromLoaded(5, "第六章", [Speech(0, 20, "六")])
        };

        foreach (var (chapterIndex, direction, expectedChapterIndex) in new[]
                 {
                     (0, -1, (int?)null),
                     (5, 1, (int?)null),
                     (0, 1, (int?)2),
                     (5, -1, (int?)2)
                 })
        {
            Assert.Equal(
                expectedChapterIndex,
                PlaybackPositionResolver.FindAdjacentChapterIndex(chapters, chapterIndex, direction));
        }
    }

    [Fact]
    public void ResolveRelativeSegmentInChapter_skips_consecutive_empty_speech_in_direction()
    {
        var chapter = PlaybackChapterContent.FromLoaded(
            0,
            "第一章",
            [
                Speech(0, 0, "第一段", "第一段"),
                Speech(1, 4, "", "仅展示一"),
                Speech(2, 8, " ", "仅展示二"),
                Speech(3, 12, "第四段", "第四段")
            ]);

        Assert.Equal(
            new PlaybackPosition(0, 3),
            PlaybackPositionResolver.ResolveRelativeSegmentInChapter(chapter, 0, 1));
        Assert.Equal(
            new PlaybackPosition(0, 0),
            PlaybackPositionResolver.ResolveRelativeSegmentInChapter(chapter, 3, -1));
        Assert.Null(PlaybackPositionResolver.ResolveRelativeSegmentInChapter(chapter, 0, -1));
        Assert.Null(PlaybackPositionResolver.ResolveRelativeSegmentInChapter(chapter, 3, 1));
    }

    [Fact]
    public void ResolveRestoredPosition_maps_offsets_and_clamps_invalid_segment_indexes()
    {
        var chapter = PlaybackChapterContent.FromLoaded(
            4,
            "第四章",
            [
                Speech(0, 0, "第一段"),
                Speech(1, 10, "第二段"),
                Speech(2, 20, "第三段")
            ]);
        var book = new PlaybackBookContent("book-1", "示例小说", [chapter]);
        foreach (var (savedSegmentIndex, characterOffset, savedAudioPosition, expectedSegmentIndex, expectedResumePosition) in new[]
                 {
                     (1, 10, 333L, 1, 333L),
                     (99, 10, 333L, 1, 0L),
                     (0, 15, 333L, 2, 0L),
                     (0, 999, 333L, 2, 0L),
                     (0, -10, 333L, 0, 0L)
                 })
        {
            var progress = new ReadingProgressEntry(
                "book-1",
                4,
                savedSegmentIndex,
                characterOffset,
                savedAudioPosition,
                DateTimeOffset.UnixEpoch);

            var result = PlaybackPositionResolver.ResolveRestoredPosition(book, progress);

            Assert.Equal(
                new PlaybackRestoredPosition(4, expectedSegmentIndex, expectedResumePosition),
                result);
        }
    }

    [Fact]
    public void FindMappedSegmentIndex_falls_back_to_last_playable_segment()
    {
        var chapter = PlaybackChapterContent.FromLoaded(
            0,
            "第一章",
            [
                Speech(0, 0, "", "过滤"),
                Speech(1, 10, "第一段", "第一段"),
                Speech(2, 20, " ", "也过滤")
            ]);

        Assert.Equal(1, PlaybackPositionResolver.FindMappedSegmentIndex(chapter, 999));
        Assert.Equal(1, PlaybackPositionResolver.FindMappedSegmentIndex(chapter, 0));
        Assert.Equal(-1, PlaybackPositionResolver.FindMappedSegmentIndex(
            PlaybackChapterContent.FromLoaded(1, "空", [Speech(0, 0, "", "")]),
            0));
    }

    [Fact]
    public void ResolveRestoredPosition_does_not_map_content_progress_back_to_title_segment()
    {
        var chapter = PlaybackChapterContent.FromLoaded(
            0,
            "第一章",
            [
                new SpeechSegment(0, 0, 0, "第一章", "第一章", true),
                Speech(1, 0, "正文")
            ]);
        var book = new PlaybackBookContent("book-1", "示例小说", [chapter]);
        var progress = new ReadingProgressEntry(
            "book-1",
            0,
            1,
            0,
            0,
            DateTimeOffset.UnixEpoch);

        var result = PlaybackPositionResolver.ResolveRestoredPosition(book, progress);

        Assert.Equal(new PlaybackRestoredPosition(0, 1, 0), result);
    }

    [Fact]
    public void ResolveRestoredPosition_does_not_treat_old_content_progress_as_title_progress()
    {
        var chapter = PlaybackChapterContent.FromLoaded(
            0,
            "第一章",
            [
                new SpeechSegment(0, 0, 0, "第一章", "第一章", true),
                Speech(1, 10, "正文")
            ]);
        var book = new PlaybackBookContent("book-1", "示例小说", [chapter]);
        var progress = new ReadingProgressEntry(
            "book-1",
            0,
            0,
            10,
            0,
            DateTimeOffset.UnixEpoch);

        var result = PlaybackPositionResolver.ResolveRestoredPosition(book, progress);

        Assert.Equal(new PlaybackRestoredPosition(0, 1, 0), result);
    }

    private static SpeechSegment Speech(
        int segmentIndex,
        int startOffset,
        string speechText,
        string? displayText = null)
    {
        return new SpeechSegment(
            segmentIndex,
            startOffset,
            speechText.Length,
            displayText ?? speechText,
            speechText);
    }
}
