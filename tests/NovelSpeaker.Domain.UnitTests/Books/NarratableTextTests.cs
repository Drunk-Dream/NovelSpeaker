using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Domain.UnitTests.Books;

public sealed class NarratableTextTests
{
    [Theory]
    [InlineData("正文")]
    [InlineData("Chapter IV")]
    [InlineData("第①章")]
    [InlineData("123")]
    [InlineData("……有人来了。")]
    public void HasContent_accepts_text_with_letters_or_numbers(string text)
    {
        Assert.True(NarratableText.HasContent(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("………")]
    [InlineData("......")]
    [InlineData("———")]
    [InlineData("***")]
    [InlineData("　…　")]
    public void HasContent_rejects_separator_only_text(string? text)
    {
        Assert.False(NarratableText.HasContent(text));
    }
}
