using System.Windows;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Input;
using NovelSpeaker.App.Shared.Presentation.Books;
using NovelSpeaker.App.Shared.Presentation.Platform;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Library;

public partial class LibraryPage : System.Windows.Controls.Page, INavigationAware, INavigableView<LibraryViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly IBookCatalogInvalidationState _catalogInvalidationState;
    private readonly IPresentationFileDialogService _fileDialogs;
    private bool _hasLoaded;

    public LibraryPage(
        LibraryViewModel viewModel,
        IBookCatalogInvalidationState catalogInvalidationState,
        IPresentationFileDialogService fileDialogs)
        : this()
    {
        _catalogInvalidationState = catalogInvalidationState;
        _fileDialogs = fileDialogs;
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

    internal LibraryPage()
    {
        _catalogInvalidationState = null!;
        _fileDialogs = null!;
        ViewModel = null!;
        InitializeComponent();
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

    private async void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ShowImportFileDialogAsync();
    }

    private void RootGrid_OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void RootGrid_OnDrop(object sender, DragEventArgs e)
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        await ViewModel.ImportFilesAsync(files ?? [], _activation.CurrentToken);
    }

    private async Task ShowImportFileDialogAsync()
    {
        var cancellationToken = _activation.CurrentToken;
        var filePath = await _fileDialogs.PickOpenFileAsync(
            new PresentationFileDialogOptions("Text files (*.txt)|*.txt|All files (*.*)|*.*"),
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            await ViewModel.ImportFilesAsync([filePath], cancellationToken);
        }
    }
}
