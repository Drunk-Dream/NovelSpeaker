using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NovelSpeaker.App.Views;

/// <summary>
/// Owns the preview list's visual centering lifecycle. It only coordinates WPF layout and scrolling;
/// playback state and user-browsing state remain outside this class.
/// </summary>
internal sealed class SegmentAutoCenterController
{
    private readonly ListBox _segmentListBox;
    private readonly Dispatcher _dispatcher;
    private readonly Func<ScrollViewer?> _getScrollViewer;
    private readonly Action _programmaticScrollStarted;
    private readonly Action _programmaticScrollCompleted;
    private readonly Func<bool> _isReducedMotionEnabled;
    private readonly Func<TimeSpan> _getAnimationDuration;

    private DispatcherTimer? _animationTimer;
    private EventHandler? _layoutUpdatedHandler;
    private int _requestVersion;
    private int _programmaticScrollDepth;
    private bool _isCenteringRequestPending;

    public SegmentAutoCenterController(
        ListBox segmentListBox,
        Dispatcher dispatcher,
        Func<ScrollViewer?> getScrollViewer,
        Action programmaticScrollStarted,
        Action programmaticScrollCompleted,
        Func<bool> isReducedMotionEnabled,
        Func<TimeSpan> getAnimationDuration)
    {
        _segmentListBox = segmentListBox;
        _dispatcher = dispatcher;
        _getScrollViewer = getScrollViewer;
        _programmaticScrollStarted = programmaticScrollStarted;
        _programmaticScrollCompleted = programmaticScrollCompleted;
        _isReducedMotionEnabled = isReducedMotionEnabled;
        _getAnimationDuration = getAnimationDuration;
    }

    public bool HasActiveAnimation => _animationTimer is not null;

    public bool IsSuppressingPassiveScroll => _isCenteringRequestPending || _programmaticScrollDepth > 0;

    public void Request(object? targetItem, bool animate)
    {
        Cancel();
        if (targetItem is null)
        {
            return;
        }

        var requestVersion = ++_requestVersion;
        _isCenteringRequestPending = true;
        _dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => CenterTarget(requestVersion, targetItem, animate)));
    }

    public void Cancel()
    {
        _requestVersion++;
        _isCenteringRequestPending = false;
        CancelLayoutWait();
        StopAnimation();
    }

    private void CenterTarget(int requestVersion, object targetItem, bool animate)
    {
        if (!IsCurrentRequest(requestVersion))
        {
            return;
        }

        var scrollViewer = _getScrollViewer();
        if (scrollViewer is null)
        {
            _isCenteringRequestPending = false;
            return;
        }

        var container = _segmentListBox.ItemContainerGenerator.ContainerFromItem(targetItem) as FrameworkElement;
        if (container is null || container.ActualHeight <= 0 || scrollViewer.ViewportHeight <= 0)
        {
            MoveTargetNearViewportCenter(targetItem, scrollViewer);
            WaitForLayout(requestVersion, targetItem, animate);
            return;
        }

        var targetOffset = CalculateCenteredOffset(container, scrollViewer);
        _isCenteringRequestPending = false;

        if (!animate || _isReducedMotionEnabled() || Math.Abs(targetOffset - scrollViewer.VerticalOffset) < 0.5d)
        {
            RunProgrammaticScroll(() => scrollViewer.ScrollToVerticalOffset(targetOffset));
            return;
        }

        StartAnimation(requestVersion, scrollViewer, targetOffset);
    }

    private void MoveTargetNearViewportCenter(object targetItem, ScrollViewer scrollViewer)
    {
        var targetIndex = _segmentListBox.Items.IndexOf(targetItem);
        if (targetIndex < 0)
        {
            _isCenteringRequestPending = false;
            return;
        }

        var (anchorIndex, anchorTop, estimatedItemHeight) = FindVisibleAnchor(scrollViewer);
        var estimatedTargetOffset = scrollViewer.VerticalOffset +
                                    anchorTop +
                                    ((targetIndex - anchorIndex) * estimatedItemHeight) -
                                    (scrollViewer.ViewportHeight / 2d);
        var clampedOffset = Math.Clamp(estimatedTargetOffset, 0d, scrollViewer.ScrollableHeight);

        if (Math.Abs(clampedOffset - scrollViewer.VerticalOffset) >= 0.5d)
        {
            RunProgrammaticScroll(() => scrollViewer.ScrollToVerticalOffset(clampedOffset));
        }
    }

    private (int Index, double Top, double ItemHeight) FindVisibleAnchor(ScrollViewer scrollViewer)
    {
        var totalHeight = 0d;
        var measuredCount = 0;
        var anchorIndex = 0;
        var anchorTop = 0d;

        for (var index = 0; index < _segmentListBox.Items.Count; index++)
        {
            if (_segmentListBox.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container ||
                container.ActualHeight <= 0)
            {
                continue;
            }

            if (measuredCount == 0)
            {
                anchorIndex = index;
                anchorTop = container.TranslatePoint(new Point(0, 0), scrollViewer).Y;
            }

            totalHeight += container.ActualHeight;
            measuredCount++;
        }

        return measuredCount > 0
            ? (anchorIndex, anchorTop, totalHeight / measuredCount)
            : (0, 0d, 88d);
    }

    private void WaitForLayout(int requestVersion, object targetItem, bool animate)
    {
        CancelLayoutWait();
        _layoutUpdatedHandler = (_, _) =>
        {
            CancelLayoutWait();
            if (IsCurrentRequest(requestVersion))
            {
                _dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() => CenterTarget(requestVersion, targetItem, animate)));
            }
        };

        _segmentListBox.LayoutUpdated += _layoutUpdatedHandler;
    }

    private void StartAnimation(int requestVersion, ScrollViewer scrollViewer, double targetOffset)
    {
        StopAnimation();

        var startOffset = scrollViewer.VerticalOffset;
        var duration = _getAnimationDuration();
        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16d)
        };

        _animationTimer = timer;
        BeginProgrammaticScroll();
        timer.Tick += (_, _) =>
        {
            if (!IsCurrentRequest(requestVersion) || !ReferenceEquals(scrollViewer, _getScrollViewer()))
            {
                StopAnimation();
                return;
            }

            var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0d, 1d);
            var easedProgress = 1d - Math.Pow(1d - progress, 3d);
            scrollViewer.ScrollToVerticalOffset(startOffset + ((targetOffset - startOffset) * easedProgress));

            if (progress >= 1d)
            {
                scrollViewer.ScrollToVerticalOffset(targetOffset);
                StopAnimation();
            }
        };

        timer.Start();
    }

    private double CalculateCenteredOffset(FrameworkElement container, ScrollViewer scrollViewer)
    {
        var top = container.TranslatePoint(new Point(0, 0), scrollViewer).Y;
        var targetOffset = scrollViewer.VerticalOffset +
                           top -
                           Math.Max(0d, (scrollViewer.ViewportHeight - container.ActualHeight) / 2d);
        return Math.Clamp(targetOffset, 0d, scrollViewer.ScrollableHeight);
    }

    private bool IsCurrentRequest(int requestVersion) => requestVersion == _requestVersion;

    private void CancelLayoutWait()
    {
        if (_layoutUpdatedHandler is null)
        {
            return;
        }

        _segmentListBox.LayoutUpdated -= _layoutUpdatedHandler;
        _layoutUpdatedHandler = null;
    }

    private void RunProgrammaticScroll(Action action)
    {
        BeginProgrammaticScroll();
        try
        {
            action();
        }
        finally
        {
            EndProgrammaticScroll();
        }
    }

    private void BeginProgrammaticScroll()
    {
        _programmaticScrollDepth++;
        _programmaticScrollStarted();
    }

    private void EndProgrammaticScroll()
    {
        _dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (_programmaticScrollDepth > 0)
                {
                    _programmaticScrollDepth--;
                }

                _programmaticScrollCompleted();
            }));
    }

    private void StopAnimation()
    {
        if (_animationTimer is null)
        {
            return;
        }

        _animationTimer.Stop();
        _animationTimer = null;
        EndProgrammaticScroll();
    }
}
