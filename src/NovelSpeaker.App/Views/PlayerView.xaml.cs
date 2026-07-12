using System.ComponentModel;
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
    private readonly SegmentAutoCenterController _segmentAutoCenterController;
    private ScrollViewer? _segmentScrollViewer;
    private bool _isKeyboardAdjustingSegmentProgress;

    public PlayerView()
    {
        InitializeComponent();
        _segmentAutoCenterController = new SegmentAutoCenterController(
            SegmentListBox,
            Dispatcher,
            () =>
            {
                InitializeSegmentScrollViewer();
                return _segmentScrollViewer;
            },
            () => IsLoaded && ActualHeight > 0 && SegmentListBox.ActualHeight > 0,
            () => _viewModel?.NotifyProgrammaticScrollStarted(),
            () => _viewModel?.NotifyProgrammaticScrollCompleted(),
            IsReducedMotionEnabled,
            () => SegmentAutoCenterAnimationDuration);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    internal TimeSpan SegmentAutoCenterAnimationDuration { get; set; } = DefaultSegmentAutoCenterAnimationDuration;

    internal bool? ReduceMotionOverride { get; set; }

    internal bool HasActiveSegmentScrollAnimation => _segmentAutoCenterController.HasActiveAnimation;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as PlayerViewModel);
        EnsureCurrentChapterVisible();
        InitializeSegmentScrollViewer();
        RequestCurrentSegmentCentering(animate: false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _segmentAutoCenterController.Cancel();
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
        _segmentAutoCenterController.Cancel();
        _viewModel?.NotifyUserScrollInput();
    }

    private void SegmentListBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsScrollKey(e.Key))
        {
            _segmentAutoCenterController.Cancel();
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
        _segmentAutoCenterController.Cancel();
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
                RequestCurrentSegmentCentering(animate: false);
            }

            return;
        }

        if (e.PropertyName == nameof(PlayerViewModel.SegmentCenterRequestVersion))
        {
            if (_viewModel?.ShouldAutoCenterCurrentSegment == true)
            {
                RequestCurrentSegmentCentering(_viewModel.AnimateNextSegmentCenterRequest);
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

    private void RequestCurrentSegmentCentering(bool animate)
    {
        InitializeSegmentScrollViewer();
        _segmentAutoCenterController.Request(_viewModel?.CurrentSegmentItem, animate);
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

        if (_segmentAutoCenterController.IsSuppressingPassiveScroll)
        {
            return;
        }

        _viewModel?.NotifyPassiveSegmentScrollChange();
    }

    private void SegmentScrollViewer_OnThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        _segmentAutoCenterController.Cancel();
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

    private bool IsReducedMotionEnabled()
    {
        return ReduceMotionOverride ?? !SystemParameters.ClientAreaAnimation;
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

}
