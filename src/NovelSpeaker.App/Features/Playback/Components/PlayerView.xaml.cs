using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovelSpeaker.App.Features.Playback.Presentation;
using NovelSpeaker.App.Features.Playback.Scrolling;
using NovelSpeaker.App.Shared.Presentation.Selection;

namespace NovelSpeaker.App.Features.Playback.Components;

public partial class PlayerView : UserControl
{
    private static readonly TimeSpan DefaultSegmentAutoCenterAnimationDuration = TimeSpan.FromMilliseconds(220);

    private PlayerViewModel? _viewModel;
    private readonly PlayerScrollInteractionController _scrollController;
    private readonly PlayerProgressInteractionController _progressController;
    private readonly PlayerSpeedCommitController _speedCommitController;

    public PlayerView()
    {
        InitializeComponent();
        _scrollController = new PlayerScrollInteractionController(
            WideChaptersListBox,
            SegmentListBox,
            Dispatcher,
            () => _viewModel,
            () => IsLoaded && ActualHeight > 0 && SegmentListBox.ActualHeight > 0,
            IsReducedMotionEnabled,
            () => SegmentAutoCenterAnimationDuration,
            isVisible => LocateCurrentChapterButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed);
        _progressController = new PlayerProgressInteractionController(
            () => _viewModel,
            () => ActivationToken);
        _speedCommitController = new PlayerSpeedCommitController(
            () => _viewModel,
            () => ActivationToken);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    internal TimeSpan SegmentAutoCenterAnimationDuration { get; set; } = DefaultSegmentAutoCenterAnimationDuration;

    internal bool? ReduceMotionOverride { get; set; }

    internal bool HasActiveSegmentScrollAnimation => _scrollController.HasActiveAnimation;

    internal CancellationToken ActivationToken { get; set; } = new(canceled: true);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as PlayerViewModel);
        _scrollController.OnLoaded();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _scrollController.OnUnloaded();
        DetachViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        AttachViewModel(e.NewValue as PlayerViewModel);
        if (IsLoaded)
        {
            _scrollController.OnLoaded();
        }
    }

    private void SegmentListBox_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _scrollController.NotifyMouseWheel();
    }

    private void SegmentListBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _scrollController.NotifyKeyDown(e.Key);
    }

    private async void ChapterButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PlayerChapterItemViewModel chapter } ||
            _viewModel is null)
        {
            return;
        }

        var modifiers = DesktopSelectionModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= DesktopSelectionModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= DesktopSelectionModifiers.Shift;
        }

        await RunEventOperationAsync(
            () => _viewModel.HandleChapterClickAsync(chapter, modifiers, ActivationToken),
            "切换章节失败");
    }

    private void LocateCurrentChapterButton_OnClick(object sender, RoutedEventArgs e)
    {
        _scrollController.LocateCurrentChapter();
    }

    private void PlayerView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.Key == Key.Escape && _viewModel.HandleActiveCacheEscape())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            _viewModel.HandleActiveCacheSelectAll())
        {
            e.Handled = true;
        }
    }

    private void SegmentProgressSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _progressController.Preview(e.NewValue);
    }

    private void SegmentProgressSlider_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Slider slider)
        {
            _progressController.OnMouseEnter(slider);
        }
    }

    private void SegmentProgressSlider_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Slider slider)
        {
            _progressController.OnMouseLeave(slider);
        }
    }

    private void SegmentProgressSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            _progressController.BeginMouse(slider);
        }
    }

    private async void SegmentProgressSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        await RunEventOperationAsync(
            () => _progressController.CommitMouseAsync(slider),
            "跳转段落失败");
    }

    private void SegmentProgressSlider_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _progressController.BeginKeyboard((Slider)sender, e.Key);
    }

    private async void SegmentProgressSlider_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        await RunEventOperationAsync(
            () => _progressController.CommitKeyboardAsync(slider, e.Key),
            "跳转段落失败");
    }

    private async void SegmentProgressSlider_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        await RunEventOperationAsync(
            () => _progressController.CommitMouseAsync(slider),
            "跳转段落失败");
    }

    private async void SpeedEditorTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        await RunEventOperationAsync(
            async () => e.Handled = await _speedCommitController.CommitOnEnterAsync(e.Key),
            "更新语速失败");
    }

    private async void SpeedEditorTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await RunEventOperationAsync(
            () => _speedCommitController.CommitOnLostFocusAsync(e.NewFocus as DependencyObject),
            "更新语速失败");
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
        _scrollController.CancelCentering();
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _scrollController.OnViewModelPropertyChanged(e);
    }

    private bool IsReducedMotionEnabled()
    {
        return ReduceMotionOverride ?? !SystemParameters.ClientAreaAnimation;
    }

    private async Task RunEventOperationAsync(Func<Task> operation, string failureTitle)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (ActivationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _viewModel?.ReportViewOperationFailure(failureTitle, exception);
        }
    }

}
