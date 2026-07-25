using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class TextSegmenterTests
{
    [Fact]
    public void Segment_returns_single_segment_for_short_single_line_paragraph()
    {
        const string chapterText = "这一段很短，不需要拆分。";

        var options = new TextSegmentationOptions(
            EnableLongParagraphSplitting: true,
            LongParagraphThreshold: 300);

        ITextSegmenter segmenter = new TextSegmenter();

        var segments = segmenter.Segment(chapterText, options);

        Assert.Single(segments);
        Assert.Equal(0, segments[0].SegmentIndex);
        Assert.Equal(0, segments[0].StartOffset);
        Assert.Equal(chapterText.Length, segments[0].Length);
        Assert.Equal(chapterText, segments[0].DisplayText);
        Assert.Equal(chapterText, segments[0].SpeechText);
    }

    [Fact]
    public void Segment_splits_each_non_blank_line_into_a_natural_paragraph_segment()
    {
        const string chapterText = "第一段。\n第二段。\n\n第三段。";

        var options = TextSegmentationOptions.Default;
        ITextSegmenter segmenter = new TextSegmenter();

        var segments = segmenter.Segment(chapterText, options);

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
        var options = new TextSegmentationOptions(false, 50);
        ITextSegmenter segmenter = new TextSegmenter();

        var segments = segmenter.Segment(text, options);

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
        var options = new TextSegmentationOptions(true, 60);
        ITextSegmenter segmenter = new TextSegmenter();

        var segments = segmenter.Segment(text, options);

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
        var options = new TextSegmentationOptions(true, 50);
        ITextSegmenter segmenter = new TextSegmenter();

        var segments = segmenter.Segment(text, options);

        Assert.Equal(3, segments.Count);
        Assert.Equal(50, segments[0].Length);
        Assert.Equal(50, segments[1].Length);
        Assert.Equal(40, segments[2].Length);
        Assert.Equal(100, segments[2].StartOffset);
    }
}
