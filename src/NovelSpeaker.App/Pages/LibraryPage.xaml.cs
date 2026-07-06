using NovelSpeaker.App.Library;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class LibraryPage : System.Windows.Controls.Page, INavigationAware, INavigableView<LibraryViewModel>
{
    private readonly IBookCatalogInvalidationState _catalogInvalidationState;
    private bool _hasLoaded;

    public LibraryPage(LibraryViewModel viewModel, IBookCatalogInvalidationState catalogInvalidationState)
    {
        _catalogInvalidationState = catalogInvalidationState;
        ViewModel = viewModel;
        InitializeComponent();
        LibraryView.DataContext = ViewModel;
    }

    public LibraryViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        if (_hasLoaded && !_catalogInvalidationState.IsInvalidated)
        {
            return;
        }

        await ViewModel.LoadAsync(CancellationToken.None);
        _hasLoaded = true;
    }

    public Task OnNavigatedFromAsync()
    {
        ViewModel.CancelActiveImport();
        return Task.CompletedTask;
    }
}
