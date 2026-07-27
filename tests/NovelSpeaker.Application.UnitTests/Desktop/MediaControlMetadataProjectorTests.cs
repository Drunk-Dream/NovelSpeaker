using NovelSpeaker.Application.Desktop.MediaControls;
using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Desktop;

public sealed class MediaControlMetadataProjectorTests
{
    [Theory]
    [InlineData(PlaybackState.Playing, MediaControlPlaybackStatus.Playing)]
    [InlineData(PlaybackState.Paused, MediaControlPlaybackStatus.Paused)]
    [InlineData(PlaybackState.Idle, MediaControlPlaybackStatus.Stopped)]
    [InlineData(PlaybackState.Preparing, MediaControlPlaybackStatus.Stopped)]
    [InlineData(PlaybackState.Buffering, MediaControlPlaybackStatus.Stopped)]
    [InlineData(PlaybackState.Stopped, MediaControlPlaybackStatus.Stopped)]
    [InlineData(PlaybackState.Recovering, MediaControlPlaybackStatus.Stopped)]
    [InlineData(PlaybackState.Faulted, MediaControlPlaybackStatus.Stopped)]
    public void Project_maps_playback_metadata_without_platform_types(
        PlaybackState state,
        MediaControlPlaybackStatus expectedStatus)
    {
        var metadata = MediaControlMetadataProjector.Project(
            CreateSnapshot(state, "第二章", "示例书"));

        Assert.Equal("第二章", metadata.ChapterTitle);
        Assert.Equal("示例书", metadata.BookTitle);
        Assert.Equal(expectedStatus, metadata.PlaybackStatus);
    }

    [Fact]
    public void Project_uses_empty_metadata_when_no_book_is_active()
    {
        var metadata = MediaControlMetadataProjector.Project(PlaybackSnapshot.Idle);

        Assert.Equal(string.Empty, metadata.ChapterTitle);
        Assert.Equal(string.Empty, metadata.BookTitle);
        Assert.Equal(MediaControlPlaybackStatus.Stopped, metadata.PlaybackStatus);
    }

    internal static PlaybackSnapshot CreateSnapshot(
        PlaybackState state,
        string? chapterTitle = "章节",
        string? bookTitle = "书名") =>
        PlaybackSnapshot.Idle with
        {
            State = state,
            BookId = "book-1",
            BookTitle = bookTitle,
            ChapterTitle = chapterTitle,
            SegmentCount = 2
        };
}
