using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class TextSegmenterTests
{
    [Fact]
    public void Segment_returns_single_segment_for_short_single_line_paragraph()
    {
        var chapter = new Chapter(
            "chapter-1",
            "book-1",
            0,
            "第一章",
            "这一段很短，不需要拆分。",
            0,
            "这一段很短，不需要拆分。".Length);

        var options = new TextSegmentationOptions(
            EnableLongParagraphSplitting: true,
            LongParagraphThreshold: 300);

        ITextSegmenter segmenter = new Infrastructure.Books.Parsing.TextSegmenter();

        var segments = segmenter.Segment(chapter, options);

        Assert.Single(segments);
        Assert.Equal(0, segments[0].SegmentIndex);
        Assert.Equal(0, segments[0].StartOffset);
        Assert.Equal(chapter.Content.Length, segments[0].Length);
        Assert.Equal(chapter.Content, segments[0].DisplayText);
        Assert.Equal(chapter.Content, segments[0].SpeechText);
    }

    [Fact]
    public void Segment_splits_each_non_blank_line_into_a_natural_paragraph_segment()
    {
        var chapter = new Chapter(
            "chapter-1",
            "book-1",
            0,
            "第一章",
            "第一段。\n第二段。\n\n第三段。",
            0,
            "第一段。\n第二段。\n\n第三段。".Length);

        var options = TextSegmentationOptions.Default;
        ITextSegmenter segmenter = new Infrastructure.Books.Parsing.TextSegmenter();

        var segments = segmenter.Segment(chapter, options);

        Assert.Equal(3, segments.Count);
        Assert.Equal("第一段。", segments[0].DisplayText);
        Assert.Equal(0, segments[0].StartOffset);
        Assert.Equal("第二段。", segments[1].DisplayText);
        Assert.Equal(5, segments[1].StartOffset);
        Assert.Equal("第三段。", segments[2].DisplayText);
        Assert.Equal(11, segments[2].StartOffset);
    }

    [Fact]
    public void Segment_keeps_long_paragraph_unchanged_when_splitting_is_disabled()
    {
        var text = string.Concat(Enumerable.Repeat("这是一句很长的话。", 40));
        var chapter = new Chapter("chapter-2", "book-1", 0, "第一章", text, 0, text.Length);
        var options = new TextSegmentationOptions(false, 50);
        ITextSegmenter segmenter = new Infrastructure.Books.Parsing.TextSegmenter();

        var segments = segmenter.Segment(chapter, options);

        Assert.Single(segments);
        Assert.Equal(text, segments[0].DisplayText);
    }

    [Fact]
    public void Segment_splits_long_paragraph_on_sentence_boundaries_before_hard_cutting()
    {
        var text = string.Concat(
            Enumerable.Repeat("这是第一句。", 12)
                .Concat(Enumerable.Repeat("这是第二句！", 12))
                .Concat(Enumerable.Repeat("这是第三句？", 12)));
        var chapter = new Chapter("chapter-3", "book-1", 0, "第一章", text, 0, text.Length);
        var options = new TextSegmentationOptions(true, 60);
        ITextSegmenter segmenter = new Infrastructure.Books.Parsing.TextSegmenter();

        var segments = segmenter.Segment(chapter, options);

        Assert.True(segments.Count > 1);
        Assert.All(
            segments,
            segment => Assert.Contains(segment.DisplayText[^1], "。！？"));
        Assert.Equal(0, segments[0].StartOffset);
        Assert.Equal(text.Length, segments.Sum(segment => segment.Length));
    }

    [Fact]
    public void Segment_hard_cuts_a_long_line_without_supported_sentence_punctuation()
    {
        var text = new string('长', 140);
        var chapter = new Chapter("chapter-4", "book-1", 0, "第一章", text, 0, text.Length);
        var options = new TextSegmentationOptions(true, 50);
        ITextSegmenter segmenter = new Infrastructure.Books.Parsing.TextSegmenter();

        var segments = segmenter.Segment(chapter, options);

        Assert.Equal(3, segments.Count);
        Assert.Equal(50, segments[0].Length);
        Assert.Equal(50, segments[1].Length);
        Assert.Equal(40, segments[2].Length);
        Assert.Equal(100, segments[2].StartOffset);
    }
}
