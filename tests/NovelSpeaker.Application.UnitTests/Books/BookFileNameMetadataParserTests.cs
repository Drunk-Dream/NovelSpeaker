using NovelSpeaker.Application.Books.Import;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Books;

public sealed class BookFileNameMetadataParserTests
{
    private readonly BookFileNameMetadataParser _parser = new();

    [Fact]
    public void Parse_extracts_title_and_author_from_default_template()
    {
        var result = _parser.Parse(
            "信息全知者 作者：魔性沧月",
            "{{name}} 作者：{{author}}");

        Assert.True(result.IsMatched);
        Assert.Equal("信息全知者", result.SuggestedTitle);
        Assert.Equal("魔性沧月", result.SuggestedAuthor);
    }

    [Fact]
    public void Parse_falls_back_to_file_name_when_template_cannot_match()
    {
        var cases = new[]
        {
            (FileName: "信息全知者-魔性沧月", Template: "{{name}} 作者：{{author}}"),
            (FileName: "信息全知者 作者：魔性沧月", Template: string.Empty),
            (FileName: "信息全知者 作者：魔性沧月", Template: "{{author}}"),
            (FileName: "信息全知者 作者：魔性沧月", Template: "{{name}} {{name}}"),
            (FileName: "信息全知者 作者：魔性沧月", Template: "{{title}} 作者：{{author}}"),
            (FileName: "信息全知者 作者：魔性沧月", Template: "《{{name}}》 作者：{{author}}")
        };

        foreach (var (fileName, template) in cases)
        {
            var result = _parser.Parse(fileName, template);

            Assert.False(result.IsMatched);
            Assert.Equal(fileName, result.SuggestedTitle);
            Assert.Null(result.SuggestedAuthor);
        }
    }

    [Fact]
    public void Parse_trims_captured_values()
    {
        var result = _parser.Parse(
            "  信息全知者  作者：  魔性沧月  ",
            "{{name}} 作者：{{author}}");

        Assert.True(result.IsMatched);
        Assert.Equal("信息全知者", result.SuggestedTitle);
        Assert.Equal("魔性沧月", result.SuggestedAuthor);
    }

}
