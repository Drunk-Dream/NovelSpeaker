using NovelSpeaker.Application.Books.Import;
using Xunit;

namespace NovelSpeaker.ApplicationTests.Books.Import;

public sealed class TextNormalizerTests
{
    [Fact]
    public void Normalize_converts_newlines_and_removes_control_characters()
    {
        var normalizer = new TextNormalizer();
        var result = normalizer.Normalize("第一章\r\n正文\u0001\r第二行\n");

        Assert.Equal("第一章\n正文\n第二行\n", result);
    }
}
