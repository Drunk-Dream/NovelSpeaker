using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NovelSpeaker.App.Features.Books.Library;

/// <summary>
/// Arranges library cards in equal-width responsive columns and virtualizes the
/// generated item containers when it is owned by an ItemsControl.
/// </summary>
public sealed class LibraryResponsivePanel : VirtualizingPanel, IScrollInfo
{
    private const double EstimatedItemHeight = 172d;

    private int _realizedStartIndex = -1;
    private int _realizedCount;
    private int _realizedColumns;
    private double _realizedItemWidth;
    private double _itemHeight;
    private double _verticalOffset;
    private double _extentHeight;
    private double _viewportHeight;
    private double _viewportWidth;
    private ScrollViewer? _scrollOwner;

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

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; } = true;

    public double ExtentWidth => _viewportWidth;

    public double ExtentHeight => _extentHeight;

    public double ViewportWidth => _viewportWidth;

    public double ViewportHeight => _viewportHeight;

    public double HorizontalOffset => 0;

    public double VerticalOffset => _verticalOffset;

    public ScrollViewer? ScrollOwner
    {
        get => _scrollOwner;
        set => _scrollOwner = value;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (ItemsControl.GetItemsOwner(this) is null)
        {
            return MeasureDirectChildren(availableSize);
        }

        var itemsControl = ItemsControl.GetItemsOwner(this)!;
        if (itemsControl is LibraryItemsControl libraryItemsControl)
        {
            libraryItemsControl.AttachScrollInfo(this);
        }

        var itemCount = itemsControl.Items.Count;
        _viewportWidth = ResolveAvailableWidth(availableSize.Width);
        var measuredViewportHeight = double.IsFinite(availableSize.Height)
            ? Math.Max(0d, availableSize.Height)
            : Math.Max(0d, _scrollOwner?.ViewportHeight ?? 0d);
        _viewportHeight = measuredViewportHeight > 0d
            ? measuredViewportHeight
            : EstimatedItemHeight * 4d;

        if (itemCount == 0 || _viewportWidth <= 0d || _viewportHeight <= 0d)
        {
            _extentHeight = 0d;
            RemoveRealizedChildren();
            InvalidateScrollInfo();
            return new Size(_viewportWidth, _viewportHeight);
        }

        var layout = CalculateLayout(_viewportWidth, itemCount);
        _itemHeight = _itemHeight > 0d ? _itemHeight : EstimatedItemHeight;
        UpdateExtent(itemCount, layout.Columns);

        var firstVisibleRow = Math.Max(
            0,
            (int)Math.Floor(_verticalOffset / (_itemHeight + VerticalSpacing)) - 1);
        var lastVisibleRow = Math.Min(
            (int)Math.Ceiling((_verticalOffset + _viewportHeight) / (_itemHeight + VerticalSpacing)),
            (int)Math.Ceiling(itemCount / (double)layout.Columns));
        var firstIndex = firstVisibleRow * layout.Columns;
        var lastIndex = Math.Min(itemCount, (lastVisibleRow + 1) * layout.Columns);
        EnsureRealizedRange(firstIndex, lastIndex - firstIndex, layout);

        var measuredItemHeight = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(layout.ItemWidth, double.PositiveInfinity));
            measuredItemHeight = Math.Max(measuredItemHeight, child.DesiredSize.Height);
        }

        if (measuredItemHeight > 0d && Math.Abs(measuredItemHeight - _itemHeight) > 0.1d)
        {
            _itemHeight = measuredItemHeight;
            UpdateExtent(itemCount, layout.Columns);
        }

        InvalidateScrollInfo();
        return new Size(_viewportWidth, _viewportHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (ItemsControl.GetItemsOwner(this) is null)
        {
            return ArrangeDirectChildren(finalSize);
        }

        var itemsControl = ItemsControl.GetItemsOwner(this)!;
        var layout = CalculateLayout(Math.Max(0d, finalSize.Width), itemsControl.Items.Count);
        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var child = InternalChildren[childIndex];
            var index = _realizedStartIndex + childIndex;

            var row = index / layout.Columns;
            var column = index % layout.Columns;
            var x = column * (layout.ItemWidth + HorizontalSpacing);
            var y = row * (_itemHeight + VerticalSpacing) - _verticalOffset;
            child.Arrange(new Rect(x, y, layout.ItemWidth, _itemHeight));
        }

        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            // A reset invalidates the generator positions before this callback
            // returns. Do not recycle the old range through that invalidated
            // generator; remove the visual children directly and let the next
            // measure establish a fresh realized range.
            RemoveRealizedChildren(recycle: false);
        }

        InvalidateMeasure();
        InvalidateArrange();
    }

    public void LineUp() => SetVerticalOffset(_verticalOffset - GetLineDelta());

    public void LineDown() => SetVerticalOffset(_verticalOffset + GetLineDelta());

    public void PageUp() => SetVerticalOffset(_verticalOffset - _viewportHeight);

    public void PageDown() => SetVerticalOffset(_verticalOffset + _viewportHeight);

    public void MouseWheelUp() => SetVerticalOffset(_verticalOffset - (GetLineDelta() * 3));

    public void MouseWheelDown() => SetVerticalOffset(_verticalOffset + (GetLineDelta() * 3));

    public void LineLeft()
    {
    }

    public void LineRight()
    {
    }

    public void PageLeft()
    {
    }

    public void PageRight()
    {
    }

    public void MouseWheelLeft()
    {
    }

    public void MouseWheelRight()
    {
    }

    public void SetHorizontalOffset(double offset)
    {
    }

    public void SetVerticalOffset(double offset)
    {
        var clamped = Math.Clamp(offset, 0d, Math.Max(0d, _extentHeight - _viewportHeight));
        if (Math.Abs(clamped - _verticalOffset) < 0.1d)
        {
            return;
        }

        _verticalOffset = clamped;
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateScrollInfo();
    }

    public bool TryGetVerticalOffset(
        int itemIndex,
        double relativeTop,
        out double verticalOffset)
    {
        verticalOffset = 0d;
        if (ItemsControl.GetItemsOwner(this) is not { } itemsControl ||
            itemIndex < 0 ||
            itemIndex >= itemsControl.Items.Count ||
            _realizedColumns <= 0 ||
            _itemHeight <= 0d)
        {
            return false;
        }

        var itemTop = (itemIndex / _realizedColumns) * (_itemHeight + VerticalSpacing);
        verticalOffset = Math.Clamp(
            itemTop - relativeTop,
            0d,
            Math.Max(0d, _extentHeight - _viewportHeight));
        return true;
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var index = ResolveItemIndex(visual);
        if (index < 0 || ItemsControl.GetItemsOwner(this) is not { } itemsControl)
        {
            return rectangle;
        }

        var layout = CalculateLayout(_viewportWidth, itemsControl.Items.Count);
        var itemTop = (index / layout.Columns) * (_itemHeight + VerticalSpacing);
        var itemBottom = itemTop + _itemHeight;
        if (itemTop < _verticalOffset)
        {
            SetVerticalOffset(itemTop);
        }
        else if (itemBottom > _verticalOffset + _viewportHeight)
        {
            SetVerticalOffset(itemBottom - _viewportHeight);
        }

        return new Rect(
            (index % layout.Columns) * (layout.ItemWidth + HorizontalSpacing),
            itemTop - _verticalOffset,
            layout.ItemWidth,
            _itemHeight);
    }

    protected override void BringIndexIntoView(int index)
    {
        if (ItemsControl.GetItemsOwner(this) is not { } itemsControl ||
            index < 0 ||
            index >= itemsControl.Items.Count)
        {
            return;
        }

        var viewportWidth = _viewportWidth > 0d
            ? _viewportWidth
            : ResolveAvailableWidth(ActualWidth);
        if (viewportWidth <= 0d)
        {
            return;
        }

        var layout = CalculateLayout(viewportWidth, itemsControl.Items.Count);
        _itemHeight = _itemHeight > 0d ? _itemHeight : EstimatedItemHeight;
        _viewportHeight = _viewportHeight > 0d ? _viewportHeight : ActualHeight;
        UpdateExtent(itemsControl.Items.Count, layout.Columns);

        var itemTop = (index / layout.Columns) * (_itemHeight + VerticalSpacing);
        var itemBottom = itemTop + _itemHeight;
        var targetOffset = itemTop < _verticalOffset
            ? itemTop
            : itemBottom > _verticalOffset + _viewportHeight
                ? itemBottom - _viewportHeight
                : _verticalOffset;
        SetVerticalOffset(targetOffset);
    }

    private Size MeasureDirectChildren(Size availableSize)
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

    private Size ArrangeDirectChildren(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        var layout = CalculateLayout(Math.Max(0d, finalSize.Width), InternalChildren.Count);
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
                var x = column * (layout.ItemWidth + HorizontalSpacing);
                InternalChildren[index].Arrange(new Rect(x, y, layout.ItemWidth, rowHeight));
            }

            y += rowHeight + VerticalSpacing;
        }

        return finalSize;
    }

    private void EnsureRealizedRange(int startIndex, int count, LayoutMetrics layout)
    {
        if (count <= 0)
        {
            RemoveRealizedChildren();
            return;
        }

        if (_realizedStartIndex == startIndex &&
            _realizedCount == count &&
            _realizedColumns == layout.Columns &&
            Math.Abs(_realizedItemWidth - layout.ItemWidth) < 0.1d)
        {
            return;
        }

        RemoveRealizedChildren();
        var generatorPosition = ItemContainerGenerator.GeneratorPositionFromIndex(startIndex);
        var childIndex = generatorPosition.Offset == 0
            ? generatorPosition.Index
            : generatorPosition.Index + 1;
        using var generator = ItemContainerGenerator.StartAt(
            generatorPosition,
            GeneratorDirection.Forward,
            allowStartAtRealizedItem: true);
        for (var index = 0; index < count; index++)
        {
            var element = ItemContainerGenerator.GenerateNext(out var isNewlyRealized) as UIElement;
            if (element is null)
            {
                break;
            }

            if (isNewlyRealized)
            {
                InsertInternalChild(childIndex++, element);
            }

            ItemContainerGenerator.PrepareItemContainer(element);
        }

        _realizedStartIndex = startIndex;
        _realizedCount = count;
        _realizedColumns = layout.Columns;
        _realizedItemWidth = layout.ItemWidth;
    }

    private void RemoveRealizedChildren(bool recycle = true)
    {
        if (InternalChildren.Count > 0)
        {
            if (recycle && ItemContainerGenerator is IRecyclingItemContainerGenerator recyclingGenerator)
            {
                recyclingGenerator.Recycle(
                    new GeneratorPosition(0, 0),
                    InternalChildren.Count);
            }

            RemoveInternalChildRange(0, InternalChildren.Count);
        }

        _realizedStartIndex = -1;
        _realizedCount = 0;
        _realizedColumns = 0;
        _realizedItemWidth = 0d;
    }

    private void UpdateExtent(int itemCount, int columns)
    {
        var rowCount = (int)Math.Ceiling(itemCount / (double)columns);
        _extentHeight = rowCount == 0
            ? 0d
            : (rowCount * _itemHeight) + (Math.Max(0, rowCount - 1) * VerticalSpacing);
        _verticalOffset = Math.Clamp(_verticalOffset, 0d, Math.Max(0d, _extentHeight - _viewportHeight));
    }

    private int ResolveItemIndex(Visual visual)
    {
        DependencyObject? current = visual;
        while (current is not null && !ReferenceEquals(current, this))
        {
            for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
            {
                if (ReferenceEquals(InternalChildren[childIndex], current))
                {
                    return _realizedStartIndex + childIndex;
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return -1;
    }

    private double GetLineDelta() => Math.Max(1d, _itemHeight + VerticalSpacing);

    private void InvalidateScrollInfo() => _scrollOwner?.InvalidateScrollInfo();

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
