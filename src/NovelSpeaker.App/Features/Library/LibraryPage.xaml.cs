using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.App.Shell.Input;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation.Books;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Library;

public partial class LibraryPage : System.Windows.Controls.Page, INavigationAware, INavigableView<LibraryViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly IBookCatalogInvalidationState _catalogInvalidationState;
    private bool _hasLoaded;

    public LibraryPage(
        LibraryViewModel viewModel,
        IBookCatalogInvalidationState catalogInvalidationState,
        ITextFilePicker textFilePicker)
    {
        _catalogInvalidationState = catalogInvalidationState;
        ViewModel = viewModel;
        InitializeComponent();
        LibraryView.DataContext = ViewModel;
        LibraryView.TextFilePicker = textFilePicker;
    }

    public LibraryViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        ViewModel.HandleNavigatedTo();
        activation.Register(ViewModel.HandleNavigatedFrom);
        if (_hasLoaded && !_catalogInvalidationState.IsInvalidated)
        {
            return;
        }

        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
            activation.TryCommit(() => _hasLoaded = true);
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
}
