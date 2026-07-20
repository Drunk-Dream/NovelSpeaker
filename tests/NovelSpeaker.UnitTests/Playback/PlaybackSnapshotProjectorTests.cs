using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed class PlaybackSnapshotProjectorTests
{
    [Fact]
    public void Project_builds_snapshot_from_explicit_input_without_coordinator_state()
    {
        var rule = new SelectedPlaybackRule(7, "测试规则", null!, null!);
        var book = new PlaybackBookContent(
            "book-1",
            "示例小说",
            [PlaybackChapterContent.FromLoaded(
                3,
                "第三章",
                [new SpeechSegment(0, 18, 4, "展示", "朗读")])],
            "作者");

        var snapshot = PlaybackSnapshotProjector.Project(new PlaybackSnapshotProjectionInput(
            PlaybackState.Buffering,
            book,
            3,
            0,
            rule,
            99,
            123,
            456,
            "正在加载",
            true,
            true,
            ContentRevision: 12));

        Assert.Equal(PlaybackState.Buffering, snapshot.State);
        Assert.Equal("book-1", snapshot.BookId);
        Assert.Equal("示例小说", snapshot.BookTitle);
        Assert.Equal("作者", snapshot.BookAuthor);
        Assert.Equal(3, snapshot.ChapterIndex);
        Assert.Equal("第三章", snapshot.ChapterTitle);
        Assert.Equal(1, snapshot.SegmentCount);
        Assert.Equal(7, snapshot.RuleId);
        Assert.Equal("测试规则", snapshot.RuleName);
        Assert.Equal(20, snapshot.SpeakSpeed);
        Assert.Equal(123, snapshot.PositionMilliseconds);
        Assert.Equal(456, snapshot.DurationMilliseconds);
        Assert.Equal("正在加载", snapshot.Message);
        Assert.True(snapshot.IsUsingCache);
        Assert.True(snapshot.CanRetry);
        Assert.True(snapshot.HasAvailableRule);
        Assert.Equal(12, snapshot.ContentRevision);
    }

    [Fact]
    public void Project_without_rule_keeps_book_context_and_uses_explicit_empty_segment_count()
    {
        var book = new PlaybackBookContent(
            "book-1",
            "示例小说",
            [PlaybackChapterContent.FromLoaded(
                0,
                "第一章",
                [new SpeechSegment(0, 0, 2, "展示", "朗读")])]);

        var snapshot = PlaybackSnapshotProjector.Project(new PlaybackSnapshotProjectionInput(
            PlaybackState.Stopped,
            book,
            0,
            0,
            null,
            0,
            0,
            0,
            "请选择规则",
            false,
            false,
            SegmentCountOverride: 0));

        Assert.Equal("book-1", snapshot.BookId);
        Assert.Equal("第一章", snapshot.ChapterTitle);
        Assert.Equal(0, snapshot.SegmentCount);
        Assert.Null(snapshot.RuleId);
        Assert.Null(snapshot.RuleName);
        Assert.Equal(10, snapshot.SpeakSpeed);
        Assert.False(snapshot.HasAvailableRule);
    }
}
