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
    public void Parse_falls_back_to_file_name_when_template_does_not_match()
    {
        var result = _parser.Parse(
            "信息全知者-魔性沧月",
            "{{name}} 作者：{{author}}");

        Assert.False(result.IsMatched);
        Assert.Equal("信息全知者-魔性沧月", result.SuggestedTitle);
        Assert.Null(result.SuggestedAuthor);
    }

    [Fact]
    public void Parse_falls_back_when_template_is_empty()
    {
        var result = _parser.Parse("信息全知者 作者：魔性沧月", string.Empty);

        Assert.False(result.IsMatched);
        Assert.Equal("信息全知者 作者：魔性沧月", result.SuggestedTitle);
        Assert.Null(result.SuggestedAuthor);
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

    [Fact]
    public void Parse_falls_back_when_template_is_invalid_or_literal_does_not_match()
    {
        foreach (var template in new[]
                 { "{{author}}", "{{name}} {{name}}", "{{title}} 作者：{{author}}", "《{{name}}》 作者：{{author}}" })
        {
            var result = _parser.Parse("信息全知者 作者：魔性沧月", template);

            Assert.False(result.IsMatched);
            Assert.Equal("信息全知者 作者：魔性沧月", result.SuggestedTitle);
            Assert.Null(result.SuggestedAuthor);
        }
    }
}
