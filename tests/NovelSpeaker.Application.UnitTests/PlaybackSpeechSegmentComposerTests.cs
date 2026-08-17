using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackSpeechSegmentComposerTests
{
    [Fact]
    public void Compose_does_not_add_a_separator_only_chapter_title()
    {
        var body = new SpeechSegment(0, 0, 2, "正文", "正文");

        var result = PlaybackSpeechSegmentComposer.Compose(
            "………",
            [body],
            readChapterTitle: true);

        Assert.Same(body, Assert.Single(result));
    }
}
