using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books.Parsing;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class ChapterSplitterTests
{
    [Fact]
    public void Split_returns_ordered_chapters_with_offsets()
    {
        ChapterRule[] rules =
        [
            new ChapterRule("1", "章节", @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$", 10, true, "now", "now")
        ];

        var text = "第一章 开始\n正文甲\n第二章 继续\n正文乙\n";
        var splitter = new ChapterSplitter();

        var chapters = splitter.Split(text, rules);

        Assert.Equal(2, chapters.Count);
        Assert.Equal("第一章 开始", chapters[0].Title);
        Assert.Equal("正文甲\n", chapters[0].Content);
        Assert.Equal(7, chapters[0].StartOffset);
        Assert.Equal("第二章 继续", chapters[1].Title);
    }

    [Fact]
    public void Split_returns_empty_when_no_non_blank_chapter_content_exists()
    {
        ChapterRule[] rules =
        [
            new ChapterRule("1", "章节", @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$", 10, true, "now", "now")
        ];

        var text = "第一章 开始\n\n第二章 继续\n\n";
        var splitter = new ChapterSplitter();

        var chapters = splitter.Split(text, rules);

        Assert.Empty(chapters);
    }
}
