using System.Windows;
using System.Windows.Controls;

namespace NovelSpeaker.App.Features.Library;

/// <summary>
/// Arranges library cards in equal-width responsive columns while keeping cards
/// within a readable width range and centering the bounded grid as a whole.
/// </summary>
public sealed class LibraryResponsivePanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth),
        typeof(double),
        typeof(LibraryResponsivePanel),
        new FrameworkPropertyMetadata(
            300d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange,
            null,
            CoercePositive));

    public static readonly DependencyProperty MaxItemWidthProperty = DependencyProperty.Register(
        nameof(MaxItemWidth),
        typeof(double),
        typeof(LibraryResponsivePanel),
        new FrameworkPropertyMetadata(
            360d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange,
            null,
            CoercePositive));

    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(LibraryResponsivePanel),
        new FrameworkPropertyMetadata(
            16d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange,
            null,
            CoerceNonNegative));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(LibraryResponsivePanel),
        new FrameworkPropertyMetadata(
            16d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange,
            null,
            CoerceNonNegative));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double MaxItemWidth
    {
        get => (double)GetValue(MaxItemWidthProperty);
        set => SetValue(MaxItemWidthProperty, value);
    }

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0)
        {
            return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0d, 0d);
        }

        var availableWidth = ResolveAvailableWidth(availableSize.Width);
        var layout = CalculateLayout(availableWidth, InternalChildren.Count);
        var itemHeight = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(layout.ItemWidth, double.PositiveInfinity));
            itemHeight = Math.Max(itemHeight, child.DesiredSize.Height);
        }

        var rowCount = (int)Math.Ceiling(InternalChildren.Count / (double)layout.Columns);
        var desiredHeight = (rowCount * itemHeight) + (Math.Max(0, rowCount - 1) * VerticalSpacing);
        var desiredWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : layout.GroupWidth;
        return new Size(desiredWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        var layout = CalculateLayout(Math.Max(0d, finalSize.Width), InternalChildren.Count);
        var startX = Math.Max(0d, (finalSize.Width - layout.GroupWidth) / 2d);
        var y = 0d;

        for (var rowStart = 0; rowStart < InternalChildren.Count; rowStart += layout.Columns)
        {
            var rowEnd = Math.Min(rowStart + layout.Columns, InternalChildren.Count);
            var rowHeight = 0d;
            for (var index = rowStart; index < rowEnd; index++)
            {
                rowHeight = Math.Max(rowHeight, InternalChildren[index].DesiredSize.Height);
            }

            for (var index = rowStart; index < rowEnd; index++)
            {
                var column = index - rowStart;
                var x = startX + (column * (layout.ItemWidth + HorizontalSpacing));
                InternalChildren[index].Arrange(new Rect(x, y, layout.ItemWidth, rowHeight));
            }

            y += rowHeight + VerticalSpacing;
        }

        return finalSize;
    }

    private LayoutMetrics CalculateLayout(double availableWidth, int itemCount)
    {
        var minWidth = MinItemWidth;
        var maxWidth = Math.Max(MinItemWidth, MaxItemWidth);
        var columnsByWidth = Math.Max(
            1,
            (int)Math.Floor((availableWidth + HorizontalSpacing) / (minWidth + HorizontalSpacing)));
        var columns = Math.Max(1, Math.Min(itemCount, columnsByWidth));
        var rawItemWidth = Math.Max(
            0d,
            (availableWidth - ((columns - 1) * HorizontalSpacing)) / columns);
        var itemWidth = Math.Min(maxWidth, rawItemWidth);
        var groupWidth = (columns * itemWidth) + ((columns - 1) * HorizontalSpacing);
        return new LayoutMetrics(columns, itemWidth, groupWidth);
    }

    private double ResolveAvailableWidth(double availableWidth)
    {
        if (double.IsFinite(availableWidth))
        {
            return Math.Max(0d, availableWidth);
        }

        return Math.Max(MinItemWidth, MaxItemWidth);
    }

    private static object CoercePositive(DependencyObject dependencyObject, object baseValue) =>
        baseValue is double value && double.IsFinite(value) && value > 0d ? value : 1d;

    private static object CoerceNonNegative(DependencyObject dependencyObject, object baseValue) =>
        baseValue is double value && double.IsFinite(value) && value >= 0d ? value : 0d;

    private readonly record struct LayoutMetrics(int Columns, double ItemWidth, double GroupWidth);
}
