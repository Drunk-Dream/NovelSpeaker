using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NovelSpeaker.App.Input;

public static class MouseWheelScrollBehavior
{
    private static readonly object ApplicationWideHandlerRegistrationGate = new();
    private static bool _isApplicationWideHandlerRegistered;

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(MouseWheelScrollBehavior),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value)
    {
        element.SetValue(EnabledProperty, value);
    }

    public static bool GetEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(EnabledProperty);
    }

    /// <summary>
    /// Enables consistent mouse-wheel scrolling for every scroll viewer in the application.
    /// Nested regions keep priority while they can scroll; at their boundary the wheel input
    /// is forwarded to the containing region.
    /// </summary>
    internal static void EnableApplicationWideHandling()
    {
        lock (ApplicationWideHandlerRegistrationGate)
        {
            if (_isApplicationWideHandlerRegistered)
            {
                return;
            }

            EventManager.RegisterClassHandler(
                typeof(ScrollViewer),
                UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(OnPreviewMouseWheel),
                handledEventsToo: true);
            _isApplicationWideHandlerRegistered = true;
        }
    }

    private static void OnEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ScrollViewer scrollViewer)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
        }
        else
        {
            scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer rootScrollViewer)
        {
            return;
        }

        if (!HandlePreviewMouseWheel(rootScrollViewer, e.OriginalSource as DependencyObject, e.Delta))
        {
            return;
        }

        e.Handled = true;
    }

    internal static bool HandlePreviewMouseWheel(ScrollViewer rootScrollViewer, DependencyObject? originalSource, int delta)
    {
        if (rootScrollViewer.ScrollableHeight <= 0)
        {
            return false;
        }

        var nestedScrollViewer = FindNestedScrollViewer(originalSource, rootScrollViewer);
        if (nestedScrollViewer is not null &&
            CanScrollInDirection(nestedScrollViewer, delta))
        {
            return false;
        }

        Scroll(rootScrollViewer, delta);
        return true;
    }

    private static ScrollViewer? FindNestedScrollViewer(DependencyObject? originalSource, ScrollViewer rootScrollViewer)
    {
        for (var current = originalSource; current is not null && !ReferenceEquals(current, rootScrollViewer); current = GetParent(current))
        {
            if (current is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }
        }

        return null;
    }

    private static bool CanScrollInDirection(ScrollViewer scrollViewer, int delta)
    {
        if (scrollViewer.ScrollableHeight <= 0)
        {
            return false;
        }

        return delta > 0
            ? scrollViewer.VerticalOffset > 0
            : scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;
    }

    private static void Scroll(ScrollViewer scrollViewer, int delta)
    {
        if (SystemParameters.WheelScrollLines < 0)
        {
            var pageOffset = scrollViewer.ViewportHeight <= 0
                ? 240d
                : scrollViewer.ViewportHeight * 0.9d;
            var targetOffset = delta > 0
                ? scrollViewer.VerticalOffset - pageOffset
                : scrollViewer.VerticalOffset + pageOffset;
            scrollViewer.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0d, scrollViewer.ScrollableHeight));

            return;
        }

        var notches = Math.Max(1d, Math.Abs(delta) / (double)Mouse.MouseWheelDeltaForOneLine);
        var lineOffset = Math.Max(16d, SystemParameters.WheelScrollLines * 16d);
        var scrollOffset = lineOffset * notches;
        var nextOffset = delta > 0
            ? scrollViewer.VerticalOffset - scrollOffset
            : scrollViewer.VerticalOffset + scrollOffset;
        scrollViewer.ScrollToVerticalOffset(Math.Clamp(nextOffset, 0d, scrollViewer.ScrollableHeight));
    }

    private static DependencyObject? GetParent(DependencyObject dependencyObject)
    {
        if (dependencyObject is Visual || dependencyObject is Visual3D)
        {
            var visualParent = VisualTreeHelper.GetParent(dependencyObject);
            if (visualParent is not null)
            {
                return visualParent;
            }
        }

        return LogicalTreeHelper.GetParent(dependencyObject);
    }
}
