using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Domain.UnitTests.Books;

public sealed class ReadingStateTests
{
    [Theory]
    [InlineData(1, 3, 1, 3)]
    [InlineData(9, 99, 1, 4)]
    [InlineData(9, 2, 1, 2)]
    [InlineData(0, 99, 0, 9)]
    [InlineData(-1, -1, 0, 0)]
    public void Clamp_preserves_ordinals_and_limits_position_to_replacement_content(
        int chapter, int offset, int expectedChapter, int expectedOffset)
    {
        var state = new ReadingState(chapter, offset);

        Assert.Equal(new ReadingState(expectedChapter, expectedOffset), state.Clamp([10, 5]));
    }

    [Fact]
    public void Clamp_empty_catalog_is_unlocated()
    {
        Assert.Null(new ReadingState(3, 20).Clamp([]));
    }

    [Fact]
    public void Clamp_empty_chapter_uses_zero_offset()
    {
        Assert.Equal(new ReadingState(0, 0), new ReadingState(0, 20).Clamp([0]));
    }
}
