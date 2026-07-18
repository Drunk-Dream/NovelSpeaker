using NovelSpeaker.Infrastructure.Speech.Legado;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class LegadoRuleSourceParserTests
{
    private readonly LegadoRuleSourceParser _parser = new();

    [Fact]
    public void Parse_preserves_array_indexes_and_reports_non_object_items()
    {
        var result = _parser.Parse(
            """[{"name":"A","url":"https://example.com/a"},42,{"name":"B","url":"https://example.com/b"}]""");

        Assert.Null(result.ErrorMessage);
        Assert.Collection(
            result.Items,
            item =>
            {
                Assert.Equal(0, item.Index);
                Assert.Equal("A", item.Source!.Name);
                Assert.Null(item.ErrorMessage);
            },
            item =>
            {
                Assert.Equal(1, item.Index);
                Assert.Null(item.Source);
                Assert.Equal("规则数组中的每一项都必须是对象。", item.ErrorMessage);
            },
            item =>
            {
                Assert.Equal(2, item.Index);
                Assert.Equal("B", item.Source!.Name);
                Assert.Null(item.ErrorMessage);
            });
    }

    [Theory]
    [InlineData("", "没有可导入的 JSON 内容。")]
    [InlineData("not-json", "JSON 解析失败，请检查规则内容。")]
    [InlineData("42", "规则 JSON 必须是对象或对象数组。")]
    public void Parse_reports_stable_top_level_errors(string json, string expectedMessage)
    {
        var result = _parser.Parse(json);

        Assert.Empty(result.Items);
        Assert.Equal(expectedMessage, result.ErrorMessage);
    }
}
