using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NovelSpeaker.App.Features.Books.Library;

/// <summary>
/// Exposes the library's virtualizing panel as the scroll client of the outer
/// ScrollViewer. The ItemsControl remains the item owner while the panel owns
/// extent, offset, and container realization.
/// </summary>
public sealed class LibraryItemsControl : ItemsControl, IScrollInfo
{
    private LibraryResponsivePanel? _scrollInfo;
    private ScrollViewer? _scrollOwner;
    private bool _canHorizontallyScroll;
    private bool _canVerticallyScroll = true;

    public static readonly DependencyProperty ItemIndexByBookIdProperty =
        DependencyProperty.Register(
            nameof(ItemIndexByBookId),
            typeof(IReadOnlyDictionary<string, int>),
            typeof(LibraryItemsControl),
            new PropertyMetadata(null));

    public IReadOnlyDictionary<string, int>? ItemIndexByBookId
    {
        get => (IReadOnlyDictionary<string, int>?)GetValue(ItemIndexByBookIdProperty);
        set => SetValue(ItemIndexByBookIdProperty, value);
    }

    internal bool TryGetItemIndex(string bookId, out int index)
    {
        if (ItemIndexByBookId is not null)
        {
            return ItemIndexByBookId.TryGetValue(bookId, out index);
        }

        index = -1;
        return false;
    }

    internal void AttachScrollInfo(LibraryResponsivePanel scrollInfo)
    {
        ArgumentNullException.ThrowIfNull(scrollInfo);

        if (ReferenceEquals(_scrollInfo, scrollInfo))
        {
            return;
        }

        _scrollInfo = scrollInfo;
        scrollInfo.CanHorizontallyScroll = _canHorizontallyScroll;
        scrollInfo.CanVerticallyScroll = _canVerticallyScroll;
        scrollInfo.ScrollOwner = _scrollOwner;
    }

    public bool CanHorizontallyScroll
    {
        get => _scrollInfo?.CanHorizontallyScroll ?? _canHorizontallyScroll;
        set
        {
            _canHorizontallyScroll = value;
            if (_scrollInfo is not null)
            {
                _scrollInfo.CanHorizontallyScroll = value;
            }
        }
    }

    public bool CanVerticallyScroll
    {
        get => _scrollInfo?.CanVerticallyScroll ?? _canVerticallyScroll;
        set
        {
            _canVerticallyScroll = value;
            if (_scrollInfo is not null)
            {
                _scrollInfo.CanVerticallyScroll = value;
            }
        }
    }

    public double ExtentWidth => _scrollInfo?.ExtentWidth ?? 0d;

    public double ExtentHeight => _scrollInfo?.ExtentHeight ?? 0d;

    public double ViewportWidth => _scrollInfo?.ViewportWidth ?? 0d;

    public double ViewportHeight => _scrollInfo?.ViewportHeight ?? 0d;

    public double HorizontalOffset => _scrollInfo?.HorizontalOffset ?? 0d;

    public double VerticalOffset => _scrollInfo?.VerticalOffset ?? 0d;

    public ScrollViewer? ScrollOwner
    {
        get => _scrollOwner;
        set
        {
            _scrollOwner = value;
            if (_scrollInfo is not null)
            {
                _scrollInfo.ScrollOwner = value;
            }
        }
    }

    public void LineUp() => _scrollInfo?.LineUp();

    public void LineDown() => _scrollInfo?.LineDown();

    public void PageUp() => _scrollInfo?.PageUp();

    public void PageDown() => _scrollInfo?.PageDown();

    public void LineLeft() => _scrollInfo?.LineLeft();

    public void LineRight() => _scrollInfo?.LineRight();

    public void PageLeft() => _scrollInfo?.PageLeft();

    public void PageRight() => _scrollInfo?.PageRight();

    public void MouseWheelUp() => _scrollInfo?.MouseWheelUp();

    public void MouseWheelDown() => _scrollInfo?.MouseWheelDown();

    public void MouseWheelLeft() => _scrollInfo?.MouseWheelLeft();

    public void MouseWheelRight() => _scrollInfo?.MouseWheelRight();

    public void SetHorizontalOffset(double offset) => _scrollInfo?.SetHorizontalOffset(offset);

    public void SetVerticalOffset(double offset) => _scrollInfo?.SetVerticalOffset(offset);

    public Rect MakeVisible(Visual visual, Rect rectangle) =>
        _scrollInfo?.MakeVisible(visual, rectangle) ?? rectangle;
}
