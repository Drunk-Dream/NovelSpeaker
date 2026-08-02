using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NovelSpeaker.App.Shared.Presentation.Scrolling;

/// <summary>
/// Owns the preview list's visual centering lifecycle. It retains the latest target until the
/// view, scroll host, and virtualized item container are all ready for a reliable calculation.
/// Playback state and user-browsing state remain outside this class.
/// </summary>
internal sealed class VirtualizedListItemCenteringController
{
    private readonly ListBox _segmentListBox;
    private readonly Dispatcher _dispatcher;
    private readonly Func<ScrollViewer?> _getScrollViewer;
    private readonly Func<bool> _isViewReady;
    private readonly Action _programmaticScrollStarted;
    private readonly Action _programmaticScrollCompleted;
    private readonly Func<bool> _isReducedMotionEnabled;
    private readonly Func<TimeSpan> _getAnimationDuration;

    private DispatcherTimer? _animationTimer;
    private object? _pendingTargetItem;
    private bool _pendingAnimate;
    private bool _isEvaluationQueued;
    private bool _areReadinessEventsAttached;
    private int _requestVersion;
    private int _programmaticScrollDepth;

    public VirtualizedListItemCenteringController(
        ListBox segmentListBox,
        Dispatcher dispatcher,
        Func<ScrollViewer?> getScrollViewer,
        Func<bool> isViewReady,
        Action programmaticScrollStarted,
        Action programmaticScrollCompleted,
        Func<bool> isReducedMotionEnabled,
        Func<TimeSpan> getAnimationDuration)
    {
        _segmentListBox = segmentListBox;
        _dispatcher = dispatcher;
        _getScrollViewer = getScrollViewer;
        _isViewReady = isViewReady;
        _programmaticScrollStarted = programmaticScrollStarted;
        _programmaticScrollCompleted = programmaticScrollCompleted;
        _isReducedMotionEnabled = isReducedMotionEnabled;
        _getAnimationDuration = getAnimationDuration;
    }

    public bool HasActiveAnimation => _animationTimer is not null;

    public bool IsSuppressingPassiveScroll => _pendingTargetItem is not null || _programmaticScrollDepth > 0;

    public void Request(object? targetItem, bool animate)
    {
        Cancel();
        if (targetItem is null)
        {
            return;
        }

        _pendingTargetItem = targetItem;
        _pendingAnimate = animate;
        _requestVersion++;
        AttachReadinessEvents();
        ScheduleEvaluation();
    }

    public void Cancel()
    {
        _requestVersion++;
        _pendingTargetItem = null;
        _isEvaluationQueued = false;
        DetachReadinessEvents();
        StopAnimation();
    }

    private void AttachReadinessEvents()
    {
        if (_areReadinessEventsAttached)
        {
            return;
        }

        _segmentListBox.Loaded += SegmentListBox_OnReadinessChanged;
        _segmentListBox.SizeChanged += SegmentListBox_OnReadinessChanged;
        _segmentListBox.LayoutUpdated += SegmentListBox_OnLayoutUpdated;
        _segmentListBox.ItemContainerGenerator.StatusChanged += ItemContainerGenerator_OnStatusChanged;
        _areReadinessEventsAttached = true;
    }

    private void DetachReadinessEvents()
    {
        if (!_areReadinessEventsAttached)
        {
            return;
        }

        _segmentListBox.Loaded -= SegmentListBox_OnReadinessChanged;
        _segmentListBox.SizeChanged -= SegmentListBox_OnReadinessChanged;
        _segmentListBox.LayoutUpdated -= SegmentListBox_OnLayoutUpdated;
        _segmentListBox.ItemContainerGenerator.StatusChanged -= ItemContainerGenerator_OnStatusChanged;
        _areReadinessEventsAttached = false;
    }

    private void SegmentListBox_OnReadinessChanged(object sender, RoutedEventArgs e)
    {
        ScheduleEvaluation();
    }

    private void SegmentListBox_OnReadinessChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleEvaluation();
    }

    private void SegmentListBox_OnLayoutUpdated(object? sender, EventArgs e)
    {
        ScheduleEvaluation();
    }

    private void ItemContainerGenerator_OnStatusChanged(object? sender, EventArgs e)
    {
        ScheduleEvaluation();
    }

    private void ScheduleEvaluation()
    {
        if (_pendingTargetItem is null || _isEvaluationQueued)
        {
            return;
        }

        _isEvaluationQueued = true;
        var requestVersion = _requestVersion;
        _dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                _isEvaluationQueued = false;
                TryCenter(requestVersion);
            }));
    }

    private void TryCenter(int requestVersion)
    {
        if (!IsCurrentRequest(requestVersion) || _pendingTargetItem is null)
        {
            return;
        }

        if (!_isViewReady())
        {
            return;
        }

        var scrollViewer = _getScrollViewer();
        if (scrollViewer is null || scrollViewer.ViewportHeight <= 0)
        {
            return;
        }

        var targetItem = _pendingTargetItem;
        var container = _segmentListBox.ItemContainerGenerator.ContainerFromItem(targetItem) as FrameworkElement;
        if (container is null || container.ActualHeight <= 0)
        {
            if (_segmentListBox.Items.Contains(targetItem))
            {
                _segmentListBox.ScrollIntoView(targetItem);
            }
            else
            {
                CompletePendingRequest();
            }

            return;
        }

        var targetOffset = CalculateCenteredOffset(container, scrollViewer);
        var animate = _pendingAnimate;
        CompletePendingRequest();

        var duration = _getAnimationDuration();
        if (!animate ||
            _isReducedMotionEnabled() ||
            duration <= TimeSpan.Zero ||
            Math.Abs(targetOffset - scrollViewer.VerticalOffset) < 0.5d)
        {
            RunProgrammaticScroll(() => scrollViewer.ScrollToVerticalOffset(targetOffset));
            return;
        }

        StartAnimation(requestVersion, scrollViewer, targetOffset, duration);
    }

    private void CompletePendingRequest()
    {
        _pendingTargetItem = null;
        _pendingAnimate = false;
        DetachReadinessEvents();
    }

    private void StartAnimation(
        int requestVersion,
        ScrollViewer scrollViewer,
        double targetOffset,
        TimeSpan duration)
    {
        StopAnimation();

        var startOffset = scrollViewer.VerticalOffset;
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

    private static double CalculateCenteredOffset(FrameworkElement container, ScrollViewer scrollViewer)
    {
        var top = container.TranslatePoint(new Point(0, 0), scrollViewer).Y;
        var targetOffset = scrollViewer.VerticalOffset +
                           top -
                           Math.Max(0d, (scrollViewer.ViewportHeight - container.ActualHeight) / 2d);
        return Math.Clamp(targetOffset, 0d, scrollViewer.ScrollableHeight);
    }

    private bool IsCurrentRequest(int requestVersion) => requestVersion == _requestVersion;

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
