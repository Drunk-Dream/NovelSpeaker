using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class BookDetailsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<BookDetailsViewModel>
{
    public BookDetailsPage(BookDetailsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PageContent.DataContext = ViewModel;
    }

    public BookDetailsViewModel ViewModel { get; }

    public BookDetailsNavigationRequest? LastRequest { get; private set; }

    public async Task OnNavigatedToAsync()
    {
        LastRequest = DataContext as BookDetailsNavigationRequest;
        if (LastRequest is null)
        {
            return;
        }

        await ViewModel.LoadAsync(LastRequest.BookId, CancellationToken.None);
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }
}
