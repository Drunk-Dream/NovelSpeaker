using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NovelSpeaker.App.Shared.Presentation.Scrolling;

/// <summary>
/// Bridges user scroll input and virtualized WPF list positioning to the platform-neutral
/// locator state. The list owns visual measurement; the state controller owns visibility rules.
/// </summary>
internal sealed class CurrentItemLocatorInteraction
{
    private readonly ListBox _listBox;
    private readonly Func<object?> _getCurrentItem;
    private readonly Action<bool> _setLocatorVisible;
    private readonly CurrentItemLocatorController _state = new();
    private readonly VirtualizedListItemCenteringController _centering;
    private ScrollViewer? _scrollViewer;
    private bool _isLoaded;
    private bool _isScrollBarPointerInteraction;

    public CurrentItemLocatorInteraction(
        ListBox listBox,
        Dispatcher dispatcher,
        Func<object?> getCurrentItem,
        Func<bool> isViewReady,
        Func<bool> isReducedMotionEnabled,
        Func<TimeSpan> getAnimationDuration,
        Action<bool> setLocatorVisible)
    {
        _listBox = listBox;
        _getCurrentItem = getCurrentItem;
        _setLocatorVisible = setLocatorVisible;
        _centering = new VirtualizedListItemCenteringController(
            listBox,
            dispatcher,
            GetScrollViewer,
            isViewReady,
            static () => { },
            static () => { },
            isReducedMotionEnabled,
            getAnimationDuration);
        _state.StateChanged += OnStateChanged;
    }

    public bool HasActiveAnimation => _centering.HasActiveAnimation;

    public void OnLoaded()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        _listBox.PreviewMouseWheel += OnPreviewMouseWheel;
        _listBox.PreviewKeyDown += OnPreviewKeyDown;
        InitializeScrollViewer();
        NotifyCurrentItemChanged(animate: false);
    }

    public void OnUnloaded()
    {
        if (!_isLoaded)
        {
            return;
        }

        _isLoaded = false;
        _listBox.PreviewMouseWheel -= OnPreviewMouseWheel;
        _listBox.PreviewKeyDown -= OnPreviewKeyDown;
        DetachScrollViewer();
        _centering.Cancel();
        _state.NotifyCurrentItemChanged();
    }

    public void NotifyCurrentItemChanged(bool animate)
    {
        _state.NotifyCurrentItemChanged();
        _centering.Request(_getCurrentItem(), animate);
    }

    public void LocateCurrentItem()
    {
        var currentItem = _getCurrentItem();
        if (currentItem is null || !_listBox.Items.Contains(currentItem))
        {
            _state.NotifyCurrentItemChanged();
            return;
        }

        if (!_state.TryBeginLocate())
        {
            return;
        }

        _centering.Request(currentItem, animate: true);
    }

    internal void NotifyUserScrollInput()
    {
        _centering.Cancel();
        _state.NotifyUserScrollInput();
    }

    private ScrollViewer? GetScrollViewer()
    {
        InitializeScrollViewer();
        return _scrollViewer;
    }

    private void InitializeScrollViewer()
    {
        if (_scrollViewer is not null)
        {
            return;
        }

        _scrollViewer = FindDescendant<ScrollViewer>(_listBox);
        if (_scrollViewer is null)
        {
            return;
        }

        _scrollViewer.ScrollChanged += OnScrollChanged;
        _scrollViewer.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnThumbDragStarted));
        _scrollViewer.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnThumbDragCompleted));
        _scrollViewer.PreviewMouseLeftButtonDown += OnScrollViewerPreviewMouseLeftButtonDown;
        _scrollViewer.PreviewMouseLeftButtonUp += OnScrollViewerPreviewMouseLeftButtonUp;
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer is null)
        {
            return;
        }

        _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnThumbDragStarted));
        _scrollViewer.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnThumbDragCompleted));
        _scrollViewer.PreviewMouseLeftButtonDown -= OnScrollViewerPreviewMouseLeftButtonDown;
        _scrollViewer.PreviewMouseLeftButtonUp -= OnScrollViewerPreviewMouseLeftButtonUp;
        _scrollViewer = null;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        NotifyUserScrollInput();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End))
        {
            return;
        }

        NotifyUserScrollInput();
    }

    private void OnThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        BeginContinuousUserScroll();
    }

    private void OnThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        EndContinuousUserScroll();
    }

    private void OnScrollViewerPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<ScrollBar>(source) is not null)
        {
            _isScrollBarPointerInteraction = true;
            BeginContinuousUserScroll();
        }
    }

    private void OnScrollViewerPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isScrollBarPointerInteraction)
        {
            return;
        }

        _isScrollBarPointerInteraction = false;
        EndContinuousUserScroll();
    }

    internal void BeginContinuousUserScroll()
    {
        _centering.Cancel();
        _state.BeginContinuousUserScroll();
    }

    internal void EndContinuousUserScroll()
    {
        ObserveCurrentItemVisibility();
        _state.EndContinuousUserScroll();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        ObserveCurrentItemVisibility();
    }

    private void ObserveCurrentItemVisibility()
    {
        var currentItem = _getCurrentItem();
        _state.ObserveCurrentItem(
            currentItem is not null,
            currentItem is not null && IsItemVisible(currentItem));
    }

    private bool IsItemVisible(object item)
    {
        if (_scrollViewer is null ||
            _listBox.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container ||
            container.ActualHeight <= 0)
        {
            return false;
        }

        var top = container.TranslatePoint(new Point(0, 0), _scrollViewer).Y;
        return top < _scrollViewer.ViewportHeight && top + container.ActualHeight > 0;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        _setLocatorVisible(_state.IsLocatorVisible);
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T ancestor)
            {
                return ancestor;
            }
        }

        return null;
    }
}
