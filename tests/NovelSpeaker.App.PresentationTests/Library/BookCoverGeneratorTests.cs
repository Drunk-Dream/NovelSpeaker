using NovelSpeaker.App.Features.Library;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Library;

public sealed class BookCoverGeneratorTests
{
    [Fact]
    public void Generate_returns_same_cover_for_same_title()
    {
        var generator = new BookCoverGenerator();

        var first = generator.Generate("三体");
        var second = generator.Generate("三体");

        Assert.Equal(first.NormalizedTitleKey, second.NormalizedTitleKey);
        Assert.Equal(first.DisplayLines, second.DisplayLines);
        Assert.Equal(first.PalettePresetId, second.PalettePresetId);
        Assert.Equal(first.DecorationPresetId, second.DecorationPresetId);
        Assert.Equal(first.ForegroundTone, second.ForegroundTone);
    }

    [Fact]
    public void Generate_normalizes_whitespace_and_case_for_cover_identity()
    {
        var generator = new BookCoverGenerator();

        var left = generator.Generate("  the   wandering earth ");
        var right = generator.Generate("THE WANDERING EARTH");

        Assert.Equal(left.NormalizedTitleKey, right.NormalizedTitleKey);
        Assert.Equal(left.PalettePresetId, right.PalettePresetId);
        Assert.Equal(left.DecorationPresetId, right.DecorationPresetId);
    }

    [Fact]
    public void Generate_splits_long_titles_into_stable_lines()
    {
        var generator = new BookCoverGenerator();

        var cover = generator.Generate("This title should be wrapped into stable lines");

        Assert.InRange(cover.DisplayLines.Count, 2, 3);
        Assert.All(cover.DisplayLines, static line => Assert.False(string.IsNullOrWhiteSpace(line)));
        Assert.True(cover.DisplayLines[^1].Length <= 8);
    }

    [Fact]
    public void Generate_uses_valid_foreground_tones_for_available_palettes()
    {
        var generator = new BookCoverGenerator();

        var covers = Enumerable
            .Range(0, 32)
            .Select(index => generator.Generate($"book-{index}"))
            .ToArray();

        Assert.Contains(covers, cover => cover.ForegroundTone == BookCoverForegroundTone.Light);
        Assert.Contains(covers, cover => cover.ForegroundTone == BookCoverForegroundTone.Dark);
        Assert.All(covers, static cover =>
        {
            Assert.InRange(cover.PalettePresetId, 0, 5);
            Assert.InRange(cover.DecorationPresetId, 0, 3);
            Assert.NotNull(cover.BackgroundBrush);
            Assert.NotNull(cover.AccentBrush);
            Assert.NotNull(cover.ForegroundBrush);
        });
    }
}
