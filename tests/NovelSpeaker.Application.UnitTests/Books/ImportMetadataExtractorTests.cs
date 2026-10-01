using System.Text.RegularExpressions;
using NovelSpeaker.Application.Books.Import;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Books;

public sealed class ImportMetadataExtractorTests
{
    private readonly ImportMetadataExtractor _extractor = new();

    [Fact]
    public void First_effective_filename_match_wins_and_header_only_fills_missing_fields()
    {
        const string text = "书名：正文书名\n作者：正文作者\n简介：正文简介\n\n第一章 开始\n正文";
        var fileNameRules = new[]
        {
            FileRule(10, @"^无效(?<name>.*)$"),
            FileRule(20, @"^(?<name>文件书名)$"),
            FileRule(30, @"^(?<author>文件书名)$")
        };
        var headerRules = new[]
        {
            HeaderRule(10, @"^书名：(?<name>.+)$"),
            HeaderRule(20, @"^作者：(?<author>.+)$"),
            HeaderRule(30, @"^简介：(?<description>.+)$"),
            HeaderRule(40, @"^作者：(?<author>覆盖作者)$")
        };

        var metadata = _extractor.Extract("文件书名", text, text.IndexOf("第一章", StringComparison.Ordinal), fileNameRules, headerRules);

        Assert.Equal("文件书名", metadata.Title);
        Assert.Equal("正文作者", metadata.Author);
        Assert.Equal("正文简介", metadata.Description);
    }

    [Fact]
    public void Filename_author_has_priority_over_header_and_name_falls_back_to_source()
    {
        var metadata = _extractor.Extract(
            "原文件名", "作者：正文作者\n简介：内容", null,
            [FileRule(10, @"^(?<author>原文件名)$")],
            [HeaderRule(10, @"^作者：(?<author>.+)$"), HeaderRule(20, @"^简介：(?<description>.+)$")]);

        Assert.Equal("原文件名", metadata.Title);
        Assert.Equal("原文件名", metadata.Author);
        Assert.Equal("内容", metadata.Description);
    }

    [Fact]
    public void Without_explicit_title_header_scans_only_bounded_prefix()
    {
        var text = string.Concat(Enumerable.Repeat("正文\n", 100)) + "作者：末尾作者\n";

        var metadata = _extractor.Extract("文件", text, null, [], [HeaderRule(10, @"^作者：(?<author>.+)$")]);

        Assert.Null(metadata.Author);
    }

    [Fact]
    public void Validation_requires_supported_capture_and_legal_regex()
    {
        ImportMetadataExtractor.ValidatePattern(@"^(?<description>.+)$");
        Assert.Throws<ArgumentException>(() => ImportMetadataExtractor.ValidatePattern(@"^(?<other>.+)$"));
        Assert.Throws<RegexParseException>(() => ImportMetadataExtractor.ValidatePattern("(?<name>"));
    }

    private static FileNameMetadataRule FileRule(int order, string pattern) =>
        new($"file-{order}", "文件名", pattern, order, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static TextHeaderMetadataRule HeaderRule(int order, string pattern) =>
        new($"header-{order}", "正文", pattern, order, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
