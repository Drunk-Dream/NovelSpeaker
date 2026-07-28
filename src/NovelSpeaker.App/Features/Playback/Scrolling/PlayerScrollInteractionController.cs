using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NovelSpeaker.App.Features.Playback.Presentation;
using NovelSpeaker.App.Shared.Presentation.Scrolling;

namespace NovelSpeaker.App.Features.Playback.Scrolling;

/// <summary>
/// Owns the Player View's scroll input, scroll host wiring, and visual centering lifecycle.
/// </summary>
internal sealed class PlayerScrollInteractionController
{
    private readonly ListBox _segmentListBox;
    private readonly Dispatcher _dispatcher;
    private readonly Func<PlayerViewModel?> _getViewModel;
    private readonly VirtualizedListItemCenteringController _autoCenterController;
    private readonly CurrentItemLocatorInteraction _chapterLocator;
    private ScrollViewer? _segmentScrollViewer;

    public PlayerScrollInteractionController(
        ListBox chapterListBox,
        ListBox segmentListBox,
        Dispatcher dispatcher,
        Func<PlayerViewModel?> getViewModel,
        Func<bool> isViewReady,
        Func<bool> isReducedMotionEnabled,
        Func<TimeSpan> getAnimationDuration,
        Action<bool> setChapterLocatorVisible)
    {
        _segmentListBox = segmentListBox;
        _dispatcher = dispatcher;
        _getViewModel = getViewModel;
        _chapterLocator = new CurrentItemLocatorInteraction(
            chapterListBox,
            dispatcher,
            () => _getViewModel()?.CurrentChapterItem,
            () => chapterListBox.IsLoaded && chapterListBox.ActualHeight > 0,
            isReducedMotionEnabled,
            getAnimationDuration,
            setChapterLocatorVisible);
        _autoCenterController = new VirtualizedListItemCenteringController(
            segmentListBox,
            dispatcher,
            GetScrollViewer,
            isViewReady,
            () => _getViewModel()?.NotifyProgrammaticScrollStarted(),
            () => _getViewModel()?.NotifyProgrammaticScrollCompleted(),
            isReducedMotionEnabled,
            getAnimationDuration);
    }

    public bool HasActiveAnimation => _autoCenterController.HasActiveAnimation;

    public void OnLoaded()
    {
        _chapterLocator.OnLoaded();
        InitializeScrollViewer();
        RequestCurrentSegmentCentering(animate: false);
    }

    public void OnUnloaded()
    {
        _autoCenterController.Cancel();
        _chapterLocator.OnUnloaded();
        DetachScrollViewer();
    }

    public void CancelCentering()
    {
        _autoCenterController.Cancel();
        _chapterLocator.OnUnloaded();
    }

    public void LocateCurrentChapter()
    {
        _chapterLocator.LocateCurrentItem();
    }

    public void NotifyMouseWheel()
    {
        _autoCenterController.Cancel();
        _getViewModel()?.NotifyUserScrollInput();
    }

    public void NotifyKeyDown(Key key)
    {
        if (key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End))
        {
            return;
        }

        _autoCenterController.Cancel();
        _getViewModel()?.NotifyUserScrollInput();
    }

    public void OnViewModelPropertyChanged(PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(PlayerViewModel.CurrentChapterItem))
        {
            _dispatcher.BeginInvoke(
                () => _chapterLocator.NotifyCurrentItemChanged(animate: false),
                DispatcherPriority.Background);
            return;
        }

        if (eventArgs.PropertyName == nameof(PlayerViewModel.CurrentSegmentItem))
        {
            if (_getViewModel()?.ShouldAutoCenterCurrentSegment == true)
            {
                RequestCurrentSegmentCentering(animate: false);
            }

            return;
        }

        if (eventArgs.PropertyName == nameof(PlayerViewModel.SegmentCenterRequestVersion) &&
            _getViewModel() is { ShouldAutoCenterCurrentSegment: true } viewModel)
        {
            RequestCurrentSegmentCentering(viewModel.AnimateNextSegmentCenterRequest);
        }
    }

    private ScrollViewer? GetScrollViewer()
    {
        InitializeScrollViewer();
        return _segmentScrollViewer;
    }

    private void RequestCurrentSegmentCentering(bool animate)
    {
        InitializeScrollViewer();
        _autoCenterController.Request(_getViewModel()?.CurrentSegmentItem, animate);
    }

    private void InitializeScrollViewer()
    {
        if (_segmentScrollViewer is not null)
        {
            return;
        }

        _segmentScrollViewer = FindDescendant<ScrollViewer>(_segmentListBox);
        if (_segmentScrollViewer is null)
        {
            return;
        }

        _segmentScrollViewer.ScrollChanged += OnScrollChanged;
        _segmentScrollViewer.AddHandler(
            Thumb.DragStartedEvent,
            new DragStartedEventHandler(OnThumbDragStarted));
        _segmentScrollViewer.AddHandler(
            Thumb.DragCompletedEvent,
            new DragCompletedEventHandler(OnThumbDragCompleted));
    }

    private void DetachScrollViewer()
    {
        if (_segmentScrollViewer is null)
        {
            return;
        }

        _segmentScrollViewer.ScrollChanged -= OnScrollChanged;
        _segmentScrollViewer.RemoveHandler(
            Thumb.DragStartedEvent,
            new DragStartedEventHandler(OnThumbDragStarted));
        _segmentScrollViewer.RemoveHandler(
            Thumb.DragCompletedEvent,
            new DragCompletedEventHandler(OnThumbDragCompleted));
        _segmentScrollViewer = null;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs eventArgs)
    {
        if (eventArgs.VerticalChange != 0 && !_autoCenterController.IsSuppressingPassiveScroll)
        {
            _getViewModel()?.NotifyPassiveSegmentScrollChange();
        }
    }

    private void OnThumbDragStarted(object sender, DragStartedEventArgs eventArgs)
    {
        _autoCenterController.Cancel();
        _getViewModel()?.NotifyScrollbarDragStarted();
    }

    private void OnThumbDragCompleted(object sender, DragCompletedEventArgs eventArgs)
    {
        _getViewModel()?.NotifyScrollbarDragCompleted();
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

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
