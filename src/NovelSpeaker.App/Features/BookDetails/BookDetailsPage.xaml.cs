using System.ComponentModel;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Shared.Presentation.Scrolling;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.BookDetails;

public partial class BookDetailsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<BookDetailsViewModel>
{
    private static readonly TimeSpan DefaultChapterLocatorAnimationDuration = TimeSpan.FromMilliseconds(220);

    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;
    private readonly CurrentItemLocatorInteraction _chapterLocator;

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
            GetChapterLocatorAnimationDuration,
            isVisible => LocateCurrentChapterButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public BookDetailsViewModel ViewModel { get; }

    public BookDetailsRoute? LastRequest { get; private set; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        activation.Register(ViewModel.HandleNavigatedFrom);
        activation.Register(_navigationGuardService.Register(ViewModel.ConfirmLeaveAsync));

        LastRequest = DataContext as BookDetailsRoute;
        if (LastRequest is null)
        {
            return;
        }

        try
        {
            await ViewModel.LoadAsync(LastRequest.BookId, activation.CancellationToken);
            if (activation.IsCurrent)
            {
                _chapterLocator.NotifyCurrentItemChanged(animate: false);
            }
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        return Task.CompletedTask;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _chapterLocator.OnLoaded();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _chapterLocator.OnUnloaded();
    }

    private void LocateCurrentChapterButton_OnClick(object sender, RoutedEventArgs e)
    {
        _chapterLocator.LocateCurrentItem();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BookDetailsViewModel.CurrentChapterItem))
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (IsLoaded)
                {
                    _chapterLocator.NotifyCurrentItemChanged(animate: false);
                }
            }));
    }

    private TimeSpan GetChapterLocatorAnimationDuration()
    {
        return ResolveAnimationDuration(TryFindResource("AnimSlow"));
    }

    internal static TimeSpan ResolveAnimationDuration(object? resource)
    {
        return resource is Duration duration && duration.HasTimeSpan
            ? duration.TimeSpan
            : DefaultChapterLocatorAnimationDuration;
    }
}
