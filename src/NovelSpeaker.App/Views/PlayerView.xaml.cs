using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Views;

public partial class PlayerView : UserControl
{
    private static readonly TimeSpan DefaultSegmentAutoCenterAnimationDuration = TimeSpan.FromMilliseconds(220);

    private PlayerViewModel? _viewModel;
    private ScrollViewer? _segmentScrollViewer;
    private DispatcherTimer? _segmentScrollAnimationTimer;
    private Action? _segmentScrollAnimationCleanup;
    private bool _isKeyboardAdjustingSegmentProgress;
    private int _segmentEnsureRequestVersion;
    private int _segmentAutoCenterSuppressionDepth;
    private int _segmentProgrammaticScrollDepth;

    public PlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    internal TimeSpan SegmentAutoCenterAnimationDuration { get; set; } = DefaultSegmentAutoCenterAnimationDuration;

    internal bool? ReduceMotionOverride { get; set; }

    internal bool HasActiveSegmentScrollAnimation => _segmentScrollAnimationTimer is not null;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as PlayerViewModel);
        EnsureCurrentChapterVisible();
        InitializeSegmentScrollViewer();
        ScheduleEnsureCurrentSegmentVisible(animate: false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelSegmentScrollAnimation();
        DetachSegmentScrollViewer();
        DetachViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        AttachViewModel(e.NewValue as PlayerViewModel);
    }

    private void SegmentListBox_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        CancelSegmentScrollAnimation();
        _viewModel?.NotifyUserScrollInput();
    }

    private void SegmentListBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsScrollKey(e.Key))
        {
            CancelSegmentScrollAnimation();
            _viewModel?.NotifyUserScrollInput();
        }
    }

    private void SegmentProgressSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_viewModel?.IsSegmentProgressDragging != true)
        {
            return;
        }

        _viewModel.PreviewSegmentProgress(e.NewValue);
    }

    private void SegmentProgressSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _viewModel?.BeginSegmentProgressInteraction();
        if (sender is Slider slider)
        {
            _viewModel?.PreviewSegmentProgress(slider.Value);
        }
    }

    private async void SegmentProgressSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || _viewModel is null || !_viewModel.IsSegmentProgressDragging)
        {
            return;
        }

        await _viewModel.CommitSegmentProgressAsync(slider.Value, CancellationToken.None);
    }

    private void SegmentProgressSlider_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null || !IsSegmentProgressKey(e.Key))
        {
            return;
        }

        if (!_isKeyboardAdjustingSegmentProgress)
        {
            _isKeyboardAdjustingSegmentProgress = true;
            _viewModel.BeginSegmentProgressInteraction();
        }
    }

    private async void SegmentProgressSlider_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (sender is not Slider slider || _viewModel is null || !_isKeyboardAdjustingSegmentProgress || !IsSegmentProgressKey(e.Key))
        {
            return;
        }

        _isKeyboardAdjustingSegmentProgress = false;
        await _viewModel.CommitSegmentProgressAsync(slider.Value, CancellationToken.None);
    }

    private async void SegmentProgressSlider_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is not Slider slider || _viewModel is null || !_viewModel.IsSegmentProgressDragging)
        {
            return;
        }

        await _viewModel.CommitSegmentProgressAsync(slider.Value, CancellationToken.None);
    }

    private async void SpeedEditorTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _viewModel is null)
        {
            return;
        }

        e.Handled = true;
        await _viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);
    }

    private async void SpeedEditorTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_viewModel is null || !_viewModel.IsSpeedMenuOpen || IsSpeedStepButton(e.NewFocus as DependencyObject))
        {
            return;
        }

        await _viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);
    }

    private void AttachViewModel(PlayerViewModel? viewModel)
    {
        if (viewModel is null || ReferenceEquals(_viewModel, viewModel))
        {
            _viewModel = viewModel;
            return;
        }

        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachViewModel()
    {
        CancelSegmentScrollAnimation();
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.CurrentChapterItem))
        {
            Dispatcher.BeginInvoke(EnsureCurrentChapterVisible, DispatcherPriority.Background);
            return;
        }

        if (e.PropertyName == nameof(PlayerViewModel.CurrentSegmentItem))
        {
            if (_viewModel?.ShouldAutoCenterCurrentSegment == true)
            {
                ScheduleEnsureCurrentSegmentVisible(animate: true);
            }

            return;
        }

        if (e.PropertyName == nameof(PlayerViewModel.SegmentCenterRequestVersion))
        {
            if (_viewModel?.ShouldAutoCenterCurrentSegment == true)
            {
                ScheduleEnsureCurrentSegmentVisible(_viewModel.AnimateNextSegmentCenterRequest);
            }
        }
    }

    private void EnsureCurrentChapterVisible()
    {
        if (_viewModel?.CurrentChapterItem is null)
        {
            return;
        }

        WideChaptersListBox.ScrollIntoView(_viewModel.CurrentChapterItem);
    }

    private void ScheduleEnsureCurrentSegmentVisible(bool animate)
    {
        CancelSegmentScrollAnimation();
        BeginSegmentAutoCenterSuppression();
        ScheduleEnsureCurrentSegmentVisible(Interlocked.Increment(ref _segmentEnsureRequestVersion), 0, animate);
    }

    private void ScheduleEnsureCurrentSegmentVisible(int requestVersion, int attempt, bool animate)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => EnsureCurrentSegmentVisible(requestVersion, attempt, animate)));
    }

    private void EnsureCurrentSegmentVisible(int requestVersion, int attempt, bool animate)
    {
        if (requestVersion != _segmentEnsureRequestVersion ||
            _viewModel?.CurrentSegmentItem is null ||
            !_viewModel.ShouldAutoCenterCurrentSegment)
        {
            CompleteSegmentAutoCenterSuppression();
            return;
        }

        InitializeSegmentScrollViewer();
        if (_segmentScrollViewer is null)
        {
            CompleteSegmentAutoCenterSuppression();
            return;
        }

        var container = SegmentListBox.ItemContainerGenerator.ContainerFromItem(_viewModel.CurrentSegmentItem) as FrameworkElement;
        if (container is null)
        {
            ScrollCurrentSegmentIntoApproximateView(attempt);
            if (attempt < 6)
            {
                ScheduleEnsureCurrentSegmentVisible(requestVersion, attempt + 1, animate);
                CompleteSegmentAutoCenterSuppression();
                return;
            }

            CompleteSegmentAutoCenterSuppression();
            return;
        }

        if (container.ActualHeight <= 0 || _segmentScrollViewer.ViewportHeight <= 0)
        {
            if (attempt < 6)
            {
                ScheduleEnsureCurrentSegmentVisible(requestVersion, attempt + 1, animate);
                CompleteSegmentAutoCenterSuppression();
                return;
            }

            CompleteSegmentAutoCenterSuppression();
            return;
        }

        var top = container.TranslatePoint(new Point(0, 0), _segmentScrollViewer).Y;
        var targetOffset = _segmentScrollViewer.VerticalOffset +
                           top -
                           Math.Max(0d, (_segmentScrollViewer.ViewportHeight - container.ActualHeight) / 2d);
        var clampedOffset = Math.Clamp(targetOffset, 0d, _segmentScrollViewer.ScrollableHeight);
        if (Math.Abs(clampedOffset - _segmentScrollViewer.VerticalOffset) < 0.5d)
        {
            CompleteSegmentAutoCenterSuppression();
            return;
        }

        if (ShouldAnimateSegmentScroll(animate, clampedOffset, attempt))
        {
            StartAnimatedSegmentScroll(clampedOffset, requestVersion, attempt > 0);
        }
        else
        {
            RunProgrammaticSegmentScroll(() => _segmentScrollViewer.ScrollToVerticalOffset(clampedOffset));
        }

        CompleteSegmentAutoCenterSuppression();
    }

    private void InitializeSegmentScrollViewer()
    {
        if (_segmentScrollViewer is not null)
        {
            return;
        }

        _segmentScrollViewer = FindDescendant<ScrollViewer>(SegmentListBox);
        if (_segmentScrollViewer is null)
        {
            return;
        }

        _segmentScrollViewer.ScrollChanged += SegmentScrollViewer_OnScrollChanged;
        _segmentScrollViewer.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(SegmentScrollViewer_OnThumbDragStarted));
        _segmentScrollViewer.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(SegmentScrollViewer_OnThumbDragCompleted));
    }

    private void DetachSegmentScrollViewer()
    {
        if (_segmentScrollViewer is null)
        {
            return;
        }

        _segmentScrollViewer.ScrollChanged -= SegmentScrollViewer_OnScrollChanged;
        _segmentScrollViewer.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(SegmentScrollViewer_OnThumbDragStarted));
        _segmentScrollViewer.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(SegmentScrollViewer_OnThumbDragCompleted));
        _segmentScrollViewer = null;
    }

    private void SegmentScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0)
        {
            return;
        }

        if (Volatile.Read(ref _segmentProgrammaticScrollDepth) > 0 ||
            Volatile.Read(ref _segmentAutoCenterSuppressionDepth) > 0)
        {
            return;
        }

        _viewModel?.NotifyUserScrollInput();
    }

    private void SegmentScrollViewer_OnThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        CancelSegmentScrollAnimation();
        _viewModel?.NotifyScrollbarDragStarted();
    }

    private void SegmentScrollViewer_OnThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _viewModel?.NotifyScrollbarDragCompleted();
    }

    private static bool IsScrollKey(Key key)
    {
        return key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;
    }

    private static bool IsSpeedStepButton(DependencyObject? target)
    {
        return target is FrameworkElement { Name: "DecreaseSpeedButton" or "IncreaseSpeedButton" };
    }

    private static bool IsSegmentProgressKey(Key key)
    {
        return key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;
    }

    private void RunProgrammaticSegmentScroll(Action action)
    {
        BeginProgrammaticSegmentScroll();

        try
        {
            action();
        }
        finally
        {
            EndProgrammaticSegmentScroll();
        }
    }

    private void BeginSegmentAutoCenterSuppression()
    {
        Interlocked.Increment(ref _segmentAutoCenterSuppressionDepth);
    }

    private void CompleteSegmentAutoCenterSuppression()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => InterlockedExtensions.DecrementIfPositive(ref _segmentAutoCenterSuppressionDepth)));
    }

    private void BeginProgrammaticSegmentScroll()
    {
        _viewModel?.NotifyProgrammaticScrollStarted();
        Interlocked.Increment(ref _segmentProgrammaticScrollDepth);
    }

    private void EndProgrammaticSegmentScroll()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                InterlockedExtensions.DecrementIfPositive(ref _segmentProgrammaticScrollDepth);
                _viewModel?.NotifyProgrammaticScrollCompleted();
            }));
    }

    private void ScrollCurrentSegmentIntoApproximateView(int attempt)
    {
        if (_viewModel?.CurrentSegmentItem is null || _segmentScrollViewer is null)
        {
            return;
        }

        if (attempt == 0)
        {
            var targetIndex = SegmentListBox.Items.IndexOf(_viewModel.CurrentSegmentItem);
            if (targetIndex >= 0)
            {
                var estimatedHeight = EstimateSegmentItemHeight();
                var estimatedOffset = (targetIndex * estimatedHeight) -
                                      Math.Max(0d, (_segmentScrollViewer.ViewportHeight - estimatedHeight) / 2d);
                RunProgrammaticSegmentScroll(() =>
                    _segmentScrollViewer.ScrollToVerticalOffset(
                        Math.Clamp(estimatedOffset, 0d, _segmentScrollViewer.ScrollableHeight)));
            }
        }

        RunProgrammaticSegmentScroll(() => SegmentListBox.ScrollIntoView(_viewModel.CurrentSegmentItem));
    }

    private double EstimateSegmentItemHeight()
    {
        double totalHeight = 0d;
        var measuredCount = 0;

        for (var index = 0; index < SegmentListBox.Items.Count; index++)
        {
            if (SegmentListBox.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container ||
                container.ActualHeight <= 0)
            {
                continue;
            }

            totalHeight += container.ActualHeight;
            measuredCount++;
            if (measuredCount >= 8)
            {
                break;
            }
        }

        return measuredCount > 0 ? totalHeight / measuredCount : 88d;
    }

    private bool ShouldAnimateSegmentScroll(bool animate, double targetOffset, int attempt)
    {
        if (!animate || _segmentScrollViewer is null || IsReducedMotionEnabled())
        {
            return false;
        }

        if (attempt > 0)
        {
            return true;
        }

        return Math.Abs(targetOffset - _segmentScrollViewer.VerticalOffset) >= 12d;
    }

    private bool IsReducedMotionEnabled()
    {
        return ReduceMotionOverride ?? !SystemParameters.ClientAreaAnimation;
    }

    private void StartAnimatedSegmentScroll(double targetOffset, int requestVersion, bool useShortDuration)
    {
        if (_segmentScrollViewer is null)
        {
            return;
        }

        CancelSegmentScrollAnimation();

        var scrollViewer = _segmentScrollViewer;
        var startOffset = scrollViewer.VerticalOffset;
        if (Math.Abs(targetOffset - startOffset) < 0.5d)
        {
            return;
        }

        var duration = useShortDuration
            ? TimeSpan.FromMilliseconds(Math.Min(SegmentAutoCenterAnimationDuration.TotalMilliseconds, 140d))
            : SegmentAutoCenterAnimationDuration;
        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16d)
        };

        var isCompleted = false;

        void Cleanup()
        {
            if (isCompleted)
            {
                return;
            }

            isCompleted = true;
            timer.Stop();
            if (ReferenceEquals(_segmentScrollAnimationTimer, timer))
            {
                _segmentScrollAnimationTimer = null;
                _segmentScrollAnimationCleanup = null;
            }

            EndProgrammaticSegmentScroll();
        }

        _segmentScrollAnimationTimer = timer;
        _segmentScrollAnimationCleanup = Cleanup;
        BeginProgrammaticSegmentScroll();

        timer.Tick += (_, _) =>
        {
            if (requestVersion != _segmentEnsureRequestVersion ||
                _segmentScrollViewer is null ||
                !ReferenceEquals(scrollViewer, _segmentScrollViewer) ||
                _viewModel?.ShouldAutoCenterCurrentSegment != true)
            {
                Cleanup();
                return;
            }

            var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0d, 1d);
            var easedProgress = 1d - Math.Pow(1d - progress, 3d);
            var nextOffset = startOffset + ((targetOffset - startOffset) * easedProgress);
            scrollViewer.ScrollToVerticalOffset(nextOffset);

            if (progress >= 1d)
            {
                scrollViewer.ScrollToVerticalOffset(targetOffset);
                Cleanup();
            }
        };

        timer.Start();
    }

    private void CancelSegmentScrollAnimation()
    {
        var cleanup = _segmentScrollAnimationCleanup;
        cleanup?.Invoke();
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static class InterlockedExtensions
    {
        public static void DecrementIfPositive(ref int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref value);
                if (current <= 0)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref value, current - 1, current) == current)
                {
                    return;
                }
            }
        }
    }
}
