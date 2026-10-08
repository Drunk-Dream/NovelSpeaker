using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Features.Books.Shared;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Features.Books;

public sealed class EffectiveReadingProgressTests
{
    [Fact]
    public void Project_ignores_snapshot_from_obsolete_source_catalog()
    {
        var current = new ActiveSourceContext("local:book", "new");
        var snapshot = PlaybackSnapshot.Idle with
        {
            BookId = "book",
            ChapterIndex = 1,
            ChapterTitle = "旧章",
            SourceContext = new("local:book", "old")
        };
        var catalog = new BookChapterSummary[] { new(0, "新一", "chapter-1", current), new(1, "新二", "chapter-2", current) };
        var progress = EffectiveReadingProgressProjector.Project("book", catalog,
            new BookReadingPosition("book", 0, 0, 0, 0, DateTimeOffset.UnixEpoch), snapshot);
        Assert.Equal(0, progress.CurrentChapterIndex);
        Assert.Equal("新一", progress.CurrentChapterTitle);
        var summary = new BookSummary("book", "显示名称", null, "新一", DateTimeOffset.UnixEpoch,
            TotalChapterCount: 2, CurrentChapterIndex: 0, SourceContext: current);
        Assert.Equal("新一", EffectiveReadingProgressProjector.Project(summary, snapshot).CurrentChapterTitle);
        var empty = EffectiveReadingProgressProjector.Project("book", [], null, snapshot);
        Assert.Null(empty.CurrentChapterIndex);
        Assert.Equal("未开始", empty.CurrentChapterTitle);
        Assert.False(empty.HasReadingProgress);
    }

    [Fact]
    public void Project_uses_catalog_order_to_resolve_chapter_identity()
    {
        var catalog = new BookChapterSummary[]
        {
            new(2, "第三章"),
            new(0, "第一章"),
            new(1, "第二章")
        };

        var progress = EffectiveReadingProgressProjector.Project(
            "book-1",
            catalog,
            new BookReadingPosition("book-1", 0, 0, 0, 0, DateTimeOffset.UtcNow),
            PlaybackSnapshot.Idle);

        Assert.Equal(0, progress.CurrentChapterIndex);
        Assert.Equal("第一章", progress.CurrentChapterTitle);
        Assert.Equal(1, progress.RemainingChapterCount);
        Assert.Equal(2d / 3d, progress.OverallProgress);
        Assert.True(progress.HasReadingProgress);
    }
}
