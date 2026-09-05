using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Features.Books.Shared;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Features.Books;

public sealed class EffectiveReadingProgressTests
{
    [Fact]
    public void Project_uses_catalog_order_to_resolve_chapter_identity()
    {
        var catalog = new BookChapterSummary[]
        {
            new(2, "第三章", 20, 10),
            new(0, "第一章", 0, 10),
            new(1, "第二章", 10, 10)
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
