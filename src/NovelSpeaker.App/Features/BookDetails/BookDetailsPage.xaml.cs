using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using System.Collections.Specialized;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.BookDetails;

public partial class BookDetailsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<BookDetailsViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;
    private bool _pendingCurrentChapterScroll;

    public BookDetailsPage(
        BookDetailsViewModel viewModel,
        INavigationGuardService navigationGuardService)
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        InitializeComponent();
        RootViewport.DataContext = ViewModel;
    }

    public BookDetailsViewModel ViewModel { get; }

    public BookDetailsRoute? LastRequest { get; private set; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        activation.Register(ViewModel.HandleNavigatedFrom);
        activation.Register(() => ViewModel.Chapters.CollectionChanged -= OnChaptersCollectionChanged);
        ViewModel.Chapters.CollectionChanged += OnChaptersCollectionChanged;
        activation.Register(_navigationGuardService.Register(ViewModel.ConfirmLeaveAsync));

        LastRequest = DataContext as BookDetailsRoute;
        if (LastRequest is null)
        {
            return;
        }

        _pendingCurrentChapterScroll = true;
        try
        {
            await ViewModel.LoadAsync(LastRequest.BookId, activation.CancellationToken);
            if (activation.IsCurrent)
            {
                await ScrollCurrentChapterIntoViewAsync(activation);
            }
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _pendingCurrentChapterScroll = false;
        _activation.Deactivate();
        return Task.CompletedTask;
    }

    private async Task ScrollCurrentChapterIntoViewAsync(PageActivationScope activation)
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
        activation.TryCommit(() => _pendingCurrentChapterScroll = false);
    }

    private void OnChaptersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_pendingCurrentChapterScroll || !IsLoaded)
        {
            return;
        }

        if (_activation.Current is { } activation)
        {
            activation.Register(ScrollCurrentChapterIntoViewAsync(activation));
        }
    }

}
