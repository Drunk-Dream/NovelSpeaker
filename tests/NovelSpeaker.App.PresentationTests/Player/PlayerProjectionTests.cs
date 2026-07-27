using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Features.Playback.Presentation;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Player;

public sealed class PlayerContentProjectionTests
{
    [Fact]
    public async Task EnsureContentLoadedAsync_projects_content_and_preserves_items_for_same_chapter()
    {
        var chapter = PlaybackChapterContent.FromLoaded(
            0,
            "第一章",
            [
                new SpeechSegment(0, 0, 3, "第一段", "第一段"),
                new SpeechSegment(1, 3, 3, "第二段", "第二段")
            ]);
        var projection = new PlayerContentProjection(
            new StubContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.Unloaded(0, "第一章")]),
                chapter));
        var snapshot = PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Paused,
            BookId = "book-1",
            ChapterIndex = 0,
            SegmentIndex = 0,
            SegmentCount = 2
        };

        await projection.EnsureContentLoadedAsync(snapshot, CancellationToken.None);
        var originalItems = projection.Segments.ToArray();
        projection.ApplyPosition(0, 1, 2);

        Assert.Equal(2, projection.CurrentChapterSegmentCount);
        Assert.Same(originalItems[1], projection.CurrentSegmentItem);
        Assert.Equal(1d, originalItems[1].VisualOpacity);
        Assert.Equal(0.82d, originalItems[0].VisualOpacity);
        Assert.All(originalItems.Select((item, index) => (item, index)), pair =>
            Assert.Same(pair.item, projection.Segments[pair.index]));
    }

    private sealed class StubContentService : IBookPlaybackContentService
    {
        private readonly PlaybackBookContent _book;
        private readonly PlaybackChapterContent _chapter;

        public StubContentService(PlaybackBookContent book, PlaybackChapterContent chapter)
        {
            _book = book;
            _chapter = chapter;
        }

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult<PlaybackBookContent?>(_book);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PlaybackChapterContent?>(_chapter);
        }
    }
}

public sealed class PlayerSnapshotProjectionTests
{
    [Fact]
    public void Project_maps_fault_and_fallback_content_without_visual_types()
    {
        var projection = new PlayerSnapshotProjection();
        var snapshot = PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Faulted,
            BookId = "book-1",
            ChapterIndex = 1,
            SegmentIndex = 2,
            SpeakSpeed = 0,
            Message = "网络失败。",
            HasAvailableRule = true
        };
        var book = new PlaybackBookContent("book-1", "示例小说", [], "作者甲");

        var result = projection.Project(snapshot, book, "第二章", defaultSpeakSpeed: 18);

        Assert.Equal("示例小说", result.Title);
        Assert.Equal("作者甲", result.Author);
        Assert.Equal("第二章", result.ChapterTitle);
        Assert.Equal("网络失败。", result.ErrorText);
        Assert.True(result.IsFaulted);
        Assert.Equal(18, result.SpeakSpeed);
    }
}
