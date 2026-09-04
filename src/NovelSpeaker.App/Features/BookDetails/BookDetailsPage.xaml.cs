using System.ComponentModel;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Shared.Presentation.Scrolling;
using NovelSpeaker.App.Shared.Theming;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.BookDetails;

public partial class BookDetailsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<BookDetailsViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;
    private readonly CurrentItemLocatorInteraction _chapterLocator;
    private bool _initialLocatorPending;
    private bool _initialLocatorEvaluationQueued;
    private bool _initialLocatorRequestIssued;
    private int _initialLocatorVersion;

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
            isVisible => LocateCurrentChapterButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed);
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
        ViewModel.DeferInitialCacheStatusProjection();
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
                ScheduleInitialChapterLocator(initialLocatorVersion);
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
        _chapterLocator.Cancel();
        _activation.Deactivate();
        return Task.CompletedTask;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        var hasPendingInitialProjection = _initialLocatorPending &&
            ViewModel.HasInitialCacheStatusProjectionPending;
        var initialLocatorVersion = _initialLocatorVersion;
        if (hasPendingInitialProjection)
        {
            _initialLocatorRequestIssued = true;
        }

        _chapterLocator.OnLoaded(
            hasPendingInitialProjection
                ? () => CompleteInitialChapterLocator(initialLocatorVersion)
                : null);
        if (!hasPendingInitialProjection)
        {
            ScheduleInitialChapterLocator(_initialLocatorVersion);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _chapterLocator.OnUnloaded();
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
        if (e.PropertyName != nameof(BookDetailsViewModel.CurrentChapterItem))
        {
            return;
        }

        if (_initialLocatorPending)
        {
            // Invalidate a readiness evaluation that may already be queued at a higher
            // dispatcher priority so it cannot center the previous snapshot item.
            _chapterLocator.Cancel();
            _initialLocatorRequestIssued = false;
            ScheduleInitialChapterLocator(_initialLocatorVersion);
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
            !ViewModel.HasInitialCacheStatusProjectionPending ||
            _initialLocatorRequestIssued ||
            _initialLocatorEvaluationQueued ||
            version != _initialLocatorVersion ||
            ViewModel.Chapters.Count == 0)
        {
            return;
        }

        _initialLocatorEvaluationQueued = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                _initialLocatorEvaluationQueued = false;
                if (!_initialLocatorPending || version != _initialLocatorVersion || !IsLoaded)
                {
                    return;
                }

                _initialLocatorRequestIssued = true;
                _chapterLocator.NotifyCurrentItemChanged(
                    animate: false,
                    completed: () => CompleteInitialChapterLocator(version));
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
        ViewModel.NotifyInitialChapterLocatorCompleted();
    }
}
