using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Pages;

public partial class LibraryPage : System.Windows.Controls.Page, IAppNavigationPage
{
    private readonly LibraryViewModel _viewModel;
    private bool _hasLoaded;

    public LibraryPage(LibraryViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        LibraryView.DataContext = viewModel;
    }

    public async Task OnNavigatedToAsync(AppNavigationEntry entry, CancellationToken cancellationToken)
    {
        if (_hasLoaded)
        {
            return;
        }

        await _viewModel.LoadAsync(cancellationToken);
        _hasLoaded = true;
    }
}
