using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Shared.Presentation.Scrolling;
using NovelSpeaker.App.Shared.Theming;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Books.Details;

public partial class BookDetailsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<BookDetailsViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;
    private readonly CurrentItemLocatorInteraction _chapterLocator;
    private ScrollViewer? _chapterScrollViewer;
    private bool _isPageLoaded;
    private bool _initialLocatorPending;
    private bool _initialLocatorEvaluationQueued;
    private bool _initialLocatorRequestIssued;
    private int _initialLocatorVersion;
    private bool _stagedLoadEvaluationQueued;

    public BookDetailsPage(
        BookDetailsViewModel viewModel,
        INavigationGuardService navigationGuardService)
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        InitializeComponent();
        RootViewport.DataContext = ViewModel;
        _chapterLocator = new CurrentItemLocatorInteraction(
            ChaptersListBox,
            Dispatcher,
            () => ViewModel.CurrentChapterItem,
            () => IsLoaded && ChaptersListBox.ActualHeight > 0,
            () => !SystemParameters.ClientAreaAnimation,
            () => MotionTokenRuntime.Slow,
            isVisible => LocateCurrentChapterButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed,
            () => ViewModel.CurrentChapterPosition);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public BookDetailsViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        var initialLocatorVersion = ++_initialLocatorVersion;
        _initialLocatorPending = true;
        _initialLocatorEvaluationQueued = false;
        _initialLocatorRequestIssued = false;
        ViewModel.HandleNavigatedTo();
        activation.Register(ViewModel.HandleNavigatedFrom);
        activation.Register(_navigationGuardService.Register(ViewModel.ConfirmLeaveAsync));

        var request = DataContext as BookDetailsRoute;
        if (request is null)
        {
            return;
        }

        try
        {
            await ViewModel.LoadAsync(request.BookId, activation.CancellationToken);
            if (activation.IsCurrent)
            {
                QueueStagedLoading(initialLocatorVersion);
            }
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _initialLocatorPending = false;
        _initialLocatorEvaluationQueued = false;
        _initialLocatorRequestIssued = false;
        _initialLocatorVersion++;
        _stagedLoadEvaluationQueued = false;
        _chapterLocator.Cancel();
        _activation.Deactivate();
        return Task.CompletedTask;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isPageLoaded = true;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _chapterLocator.OnLoaded();
        AttachChapterViewport();
        QueueStagedLoading(_initialLocatorVersion);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isPageLoaded = false;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _chapterLocator.OnUnloaded();
        DetachChapterViewport();
    }

    private void AttachChapterViewport()
    {
        if (_chapterScrollViewer is not null)
        {
            return;
        }

        _chapterScrollViewer = FindDescendant<ScrollViewer>(ChaptersListBox);
        if (_chapterScrollViewer is null)
        {
            return;
        }

        _chapterScrollViewer.ScrollChanged += ChapterScrollViewer_OnScrollChanged;
        RequestVisibleChapterDecorationWindow();
    }

    private void DetachChapterViewport()
    {
        if (_chapterScrollViewer is null)
        {
            return;
        }

        _chapterScrollViewer.ScrollChanged -= ChapterScrollViewer_OnScrollChanged;
        _chapterScrollViewer = null;
    }

    private void ChapterScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.ViewportHeightChange != 0 || e.ExtentHeightChange != 0)
        {
            RequestVisibleChapterDecorationWindow();
        }
    }

    private void RequestVisibleChapterDecorationWindow()
    {
        QueueStagedLoading(_initialLocatorVersion);
    }

    private void ApplyVisibleChapterDecorationWindow()
    {
        if (_chapterScrollViewer is null)
        {
            return;
        }

        var (start, count) = VirtualizedCatalogViewport.GetWindow(
            ChaptersListBox,
            _chapterScrollViewer,
            ViewModel.Chapters.Count,
            32);
        ViewModel.RequestCacheDecorationWindow(start, count);
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

    private void LocateCurrentChapterButton_OnClick(object sender, RoutedEventArgs e)
    {
        _chapterLocator.LocateCurrentItem(
            _initialLocatorPending
                ? () => CompleteInitialChapterLocator(_initialLocatorVersion)
                : null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BookDetailsViewModel.IsChapterCatalogReady))
        {
            QueueStagedLoading(_initialLocatorVersion);
            return;
        }

        if (e.PropertyName != nameof(BookDetailsViewModel.CurrentChapterItem))
        {
            return;
        }

        if (_initialLocatorPending)
        {
            _chapterLocator.Cancel();
            _initialLocatorRequestIssued = false;
            QueueStagedLoading(_initialLocatorVersion);
            return;
        }

        var version = _initialLocatorVersion;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (IsLoaded && version == _initialLocatorVersion)
                {
                    _chapterLocator.NotifyCurrentItemChanged(animate: false);
                }
            }));
    }

    private void ScheduleInitialChapterLocator(int version)
    {
        if (!_initialLocatorPending ||
            _initialLocatorRequestIssued ||
            _initialLocatorEvaluationQueued ||
            version != _initialLocatorVersion ||
            !ViewModel.IsChapterCatalogReady ||
            ViewModel.CurrentChapterItem is null)
        {
            return;
        }

        _initialLocatorEvaluationQueued = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                _initialLocatorEvaluationQueued = false;
                if (!_initialLocatorPending || version != _initialLocatorVersion || !_isPageLoaded)
                {
                    return;
                }

                _initialLocatorRequestIssued = true;
                _chapterLocator.NotifyCurrentItemChanged(
                    animate: false,
                    completed: () => CompleteInitialChapterLocator(version));
            }));
    }

    private void QueueStagedLoading(int version)
    {
        if (_stagedLoadEvaluationQueued ||
            version != _initialLocatorVersion ||
            !_isPageLoaded ||
            _activation.Current is not { IsCurrent: true } ||
            !ViewModel.IsChapterCatalogReady)
        {
            return;
        }

        _stagedLoadEvaluationQueued = true;
        // ContextIdle runs after the current render work, so synchronous query
        // completions cannot move the catalog projection into the first frame.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                _stagedLoadEvaluationQueued = false;
                if (version == _initialLocatorVersion &&
                    _isPageLoaded &&
                    _activation.Current is { IsCurrent: true })
                {
                    ApplyVisibleChapterDecorationWindow();
                    ScheduleInitialChapterLocator(version);
                    ViewModel.StartStagedLoading();
                }
            }));
    }

    private void CompleteInitialChapterLocator(int version)
    {
        if (!_initialLocatorPending || version != _initialLocatorVersion)
        {
            return;
        }

        _initialLocatorPending = false;
        _initialLocatorRequestIssued = false;
    }
}
