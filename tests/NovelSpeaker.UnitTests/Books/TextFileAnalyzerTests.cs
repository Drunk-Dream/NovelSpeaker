using System.Text;
using NovelSpeaker.Infrastructure.Books.Text;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class TextFileAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_reads_utf8_file_and_returns_preview()
    {
        var filePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(filePath, "第一章 开始\n正文一\n正文二", new UTF8Encoding(false));

        var analyzer = new TextFileAnalyzer();
        var result = await analyzer.AnalyzeAsync(filePath, encodingName: null, CancellationToken.None);

        Assert.Equal("utf-8", result.EncodingName);
        Assert.Contains("第一章 开始", result.PreviewText);
        Assert.Contains("正文一", result.RawText);
    }

    [Fact]
    public async Task AnalyzeAsync_falls_back_to_gb18030_when_strict_utf8_fails()
    {
        var filePath = Path.GetTempFileName();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gb18030 = Encoding.GetEncoding("GB18030");
        await File.WriteAllTextAsync(filePath, "第一章 回退\n正文", gb18030);

        var analyzer = new TextFileAnalyzer();
        var result = await analyzer.AnalyzeAsync(filePath, encodingName: null, CancellationToken.None);

        Assert.Equal("gb18030", result.EncodingName);
        Assert.Contains("第一章 回退", result.RawText);
    }
}
