using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class BookDetailsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<BookDetailsViewModel>
{
    private readonly INavigationGuardService _navigationGuardService;
    private IDisposable? _guardRegistration;

    public BookDetailsPage(
        BookDetailsViewModel viewModel,
        INavigationGuardService navigationGuardService)
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        InitializeComponent();
        RootLayout.DataContext = ViewModel;
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

        await ViewModel.LoadAsync(LastRequest.BookId, CancellationToken.None);
        await ScrollCurrentChapterIntoViewAsync();
    }

    public Task OnNavigatedFromAsync()
    {
        _guardRegistration?.Dispose();
        _guardRegistration = null;
        return Task.CompletedTask;
    }

    private async Task ScrollCurrentChapterIntoViewAsync()
    {
        if (ViewModel.CurrentChapterItem is null)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            ChaptersListBox.UpdateLayout();
            ChaptersListBox.ScrollIntoView(ViewModel.CurrentChapterItem);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
