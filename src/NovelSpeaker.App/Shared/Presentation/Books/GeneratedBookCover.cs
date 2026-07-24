using System.Collections.ObjectModel;
using System.Windows.Media;

namespace NovelSpeaker.App.Shared.Presentation.Books;

public sealed class GeneratedBookCover
{
    public GeneratedBookCover(
        string normalizedTitleKey,
        IReadOnlyList<string> displayLines,
        int palettePresetId,
        int decorationPresetId,
        BookCoverForegroundTone foregroundTone,
        Color startColor,
        Color endColor,
        Color accentColor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedTitleKey);
        ArgumentNullException.ThrowIfNull(displayLines);

        NormalizedTitleKey = normalizedTitleKey;
        DisplayLines = new ReadOnlyCollection<string>(displayLines.ToArray());
        PalettePresetId = palettePresetId;
        DecorationPresetId = decorationPresetId;
        ForegroundTone = foregroundTone;
        BackgroundBrush = CreateBackgroundBrush(startColor, endColor);
        AccentBrush = CreateSolidBrush(accentColor);
        ForegroundBrush = CreateSolidBrush(
            foregroundTone == BookCoverForegroundTone.Light
                ? Color.FromRgb(248, 250, 252)
                : Color.FromRgb(24, 28, 36));
    }

    public string NormalizedTitleKey { get; }

    public IReadOnlyList<string> DisplayLines { get; }

    public int PalettePresetId { get; }

    public int DecorationPresetId { get; }

    public BookCoverForegroundTone ForegroundTone { get; }

    public LinearGradientBrush BackgroundBrush { get; }

    public SolidColorBrush AccentBrush { get; }

    public SolidColorBrush ForegroundBrush { get; }

    private static LinearGradientBrush CreateBackgroundBrush(Color startColor, Color endColor)
    {
        var brush = new LinearGradientBrush(startColor, endColor, 135);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateSolidBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
