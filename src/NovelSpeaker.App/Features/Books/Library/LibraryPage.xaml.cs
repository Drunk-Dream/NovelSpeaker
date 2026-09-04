using System.Windows;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Input;
using NovelSpeaker.App.Features.Books.Shared;
using NovelSpeaker.App.Shared.Presentation.Platform;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Books.Library;

public partial class LibraryPage : System.Windows.Controls.Page, INavigationAware, INavigableView<LibraryViewModel>, IKeyboardShortcutTarget
{
    private readonly PageActivationController _activation = new();
    private readonly IBookCatalogInvalidationState _catalogInvalidationState;
    private readonly IPresentationFileDialogService _fileDialogs;
    private readonly PageEventOperationRunner _eventOperations;
    private readonly IKeyboardShortcutTargetRegistry? _shortcutTargets;
    private bool _hasLoaded;

    public LibraryPage(
        LibraryViewModel viewModel,
        IBookCatalogInvalidationState catalogInvalidationState,
        IPresentationFileDialogService fileDialogs,
        PageEventOperationRunner eventOperations,
        IKeyboardShortcutTargetRegistry? shortcutTargets = null)
        : this()
    {
        _catalogInvalidationState = catalogInvalidationState;
        _fileDialogs = fileDialogs;
        _eventOperations = eventOperations;
        _shortcutTargets = shortcutTargets;
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

    internal LibraryPage()
    {
        _catalogInvalidationState = null!;
        _fileDialogs = null!;
        _eventOperations = PageEventOperationRunner.DesignTime;
        _shortcutTargets = null;
        ViewModel = null!;
        InitializeComponent();
    }

    public LibraryViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        ViewModel.HandleNavigatedTo();
        activation.Register(ViewModel.HandleNavigatedFrom);
        if (_shortcutTargets is not null)
        {
            activation.Register(_shortcutTargets.Register(this));
        }
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

    public async Task<bool> HandleKeyboardShortcutAsync(
        KeyboardShortcutAction action,
        string? argument,
        CancellationToken cancellationToken)
    {
        if (action != KeyboardShortcutAction.ImportTextFile ||
            string.IsNullOrWhiteSpace(argument) ||
            _activation.Current is not { IsCurrent: true } activation)
        {
            return false;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            activation.CancellationToken);
        await ViewModel.ImportFilesAsync([argument], linkedCancellation.Token).ConfigureAwait(true);
        return true;
    }

    private async void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunEventOperationAsync("导入失败", ShowImportFileDialogAsync);
    }

    private void RootGrid_OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void RootGrid_OnDrop(object sender, DragEventArgs e)
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        await RunEventOperationAsync(
            "导入失败",
            cancellationToken => ViewModel.ImportFilesAsync(files ?? [], cancellationToken));
    }

    private async Task ShowImportFileDialogAsync(CancellationToken cancellationToken)
    {
        var filePath = await _fileDialogs.PickOpenFileAsync(
            new PresentationFileDialogOptions("Text files (*.txt)|*.txt|All files (*.*)|*.*"),
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            await ViewModel.ImportFilesAsync([filePath], cancellationToken);
        }
    }

    private Task RunEventOperationAsync(
        string failureTitle,
        Func<CancellationToken, Task> operation)
    {
        return _eventOperations.RunAsync(_activation, failureTitle, operation);
    }
}
