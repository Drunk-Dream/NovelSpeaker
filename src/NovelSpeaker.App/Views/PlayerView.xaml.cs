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
    private PlayerViewModel? _viewModel;
    private ScrollViewer? _segmentScrollViewer;
    private bool _isKeyboardAdjustingSegmentProgress;

    public PlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as PlayerViewModel);
        _viewModel?.UpdateLayoutWidth(ActualWidth);
        EnsureCurrentChapterVisible();
        InitializeSegmentScrollViewer();
        Dispatcher.BeginInvoke(EnsureCurrentSegmentVisible, DispatcherPriority.Background);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachSegmentScrollViewer();
        DetachViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        AttachViewModel(e.NewValue as PlayerViewModel);
    }

    private void PlayerView_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel?.UpdateLayoutWidth(e.NewSize.Width);
    }

    private void SegmentListBox_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _viewModel?.NotifyUserScrollInput();
    }

    private void SegmentListBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsScrollKey(e.Key))
        {
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

        if (e.PropertyName == nameof(PlayerViewModel.CurrentSegmentItem) ||
            e.PropertyName == nameof(PlayerViewModel.ShouldAutoCenterCurrentSegment))
        {
            if (_viewModel?.ShouldAutoCenterCurrentSegment == true)
            {
                Dispatcher.BeginInvoke(EnsureCurrentSegmentVisible, DispatcherPriority.Background);
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
        DrawerChaptersListBox.ScrollIntoView(_viewModel.CurrentChapterItem);
    }

    private void EnsureCurrentSegmentVisible()
    {
        if (_viewModel?.CurrentSegmentItem is null || !_viewModel.ShouldAutoCenterCurrentSegment)
        {
            return;
        }

        InitializeSegmentScrollViewer();
        if (_segmentScrollViewer is null)
        {
            return;
        }

        _viewModel.NotifyProgrammaticScrollStarted();
        try
        {
            SegmentListBox.ScrollIntoView(_viewModel.CurrentSegmentItem);
            SegmentListBox.UpdateLayout();

            if (SegmentListBox.ItemContainerGenerator.ContainerFromItem(_viewModel.CurrentSegmentItem) is not FrameworkElement container)
            {
                return;
            }

            var top = container.TranslatePoint(new Point(0, 0), _segmentScrollViewer).Y;
            var targetOffset = _segmentScrollViewer.VerticalOffset +
                               top -
                               Math.Max(0d, (_segmentScrollViewer.ViewportHeight - container.ActualHeight) / 2d);
            _segmentScrollViewer.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0d, _segmentScrollViewer.ScrollableHeight));
        }
        finally
        {
            _viewModel.NotifyProgrammaticScrollCompleted();
        }
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

        _viewModel?.NotifyUserScrollInput();
    }

    private void SegmentScrollViewer_OnThumbDragStarted(object sender, DragStartedEventArgs e)
    {
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

    private static bool IsSegmentProgressKey(Key key)
    {
        return key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;
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
