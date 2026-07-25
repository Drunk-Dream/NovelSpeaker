using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Domain.UnitTests.Books;

public sealed class TextSegmentationOptionsTests
{
    [Fact]
    public void Normalize_clamps_threshold_to_domain_minimum()
    {
        var options = new TextSegmentationOptions(true, 1);

        var normalized = options.Normalize();

        Assert.Equal(TextSegmentationOptions.MinimumLongParagraphThreshold, normalized.LongParagraphThreshold);
    }

    [Fact]
    public void Default_uses_enabled_segmentation_and_default_threshold()
    {
        var options = TextSegmentationOptions.Default;

        Assert.True(options.EnableLongParagraphSplitting);
        Assert.Equal(TextSegmentationOptions.DefaultLongParagraphThreshold, options.LongParagraphThreshold);
    }
}
