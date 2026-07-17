using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using System.Collections.Specialized;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class BookDetailsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<BookDetailsViewModel>
{
    private readonly INavigationGuardService _navigationGuardService;
    private IDisposable? _guardRegistration;
    private bool _pendingCurrentChapterScroll;

    public BookDetailsPage(
        BookDetailsViewModel viewModel,
        INavigationGuardService navigationGuardService)
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        InitializeComponent();
        RootViewport.DataContext = ViewModel;
        ViewModel.Chapters.CollectionChanged += OnChaptersCollectionChanged;
    }

    public BookDetailsViewModel ViewModel { get; }

    public BookDetailsNavigationRequest? LastRequest { get; private set; }

    public async Task OnNavigatedToAsync()
    {
        _guardRegistration?.Dispose();
        _guardRegistration = _navigationGuardService.Register(ViewModel.ConfirmLeaveAsync);

        LastRequest = DataContext as BookDetailsNavigationRequest;
        if (LastRequest is null)
        {
            return;
        }

        _pendingCurrentChapterScroll = true;
        await ViewModel.LoadAsync(LastRequest.BookId, CancellationToken.None);
        await ScrollCurrentChapterIntoViewAsync();
    }

    public Task OnNavigatedFromAsync()
    {
        _guardRegistration?.Dispose();
        _guardRegistration = null;
        _pendingCurrentChapterScroll = false;
        ViewModel.HandleNavigatedFrom();
        return Task.CompletedTask;
    }

    private async Task ScrollCurrentChapterIntoViewAsync()
    {
        if (!_pendingCurrentChapterScroll || ViewModel.CurrentChapterItem is null)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            ChaptersListBox.UpdateLayout();
            ChaptersListBox.ScrollIntoView(ViewModel.CurrentChapterItem);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
        _pendingCurrentChapterScroll = false;
    }

    private void OnChaptersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_pendingCurrentChapterScroll || !IsLoaded)
        {
            return;
        }

        _ = ScrollCurrentChapterIntoViewAsync();
    }

}
