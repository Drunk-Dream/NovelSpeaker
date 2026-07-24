using System.Windows;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.TtsRules;

public partial class TtsRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<TtsRulesViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;
    private readonly IPresentationFileDialogService _fileDialogs;
    private readonly IPresentationClipboard _clipboard;
    private bool _hasLoaded;

    public TtsRulesPage(
        TtsRulesViewModel viewModel,
        INavigationGuardService navigationGuardService,
        IPresentationFileDialogService fileDialogs,
        IPresentationClipboard clipboard)
        : this()
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        _fileDialogs = fileDialogs;
        _clipboard = clipboard;
        DataContext = ViewModel;
    }

    internal TtsRulesPage()
    {
        ViewModel = null!;
        _navigationGuardService = null!;
        _fileDialogs = null!;
        _clipboard = null!;
        InitializeComponent();
    }

    public TtsRulesViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        activation.Register(ViewModel.HandleNavigatedFrom);
        activation.Register(_navigationGuardService.Register(ViewModel.ConfirmLeaveAsync));

        if (_hasLoaded)
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

    private async void ImportFromFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var cancellationToken = _activation.CurrentToken;
        var filePath = await _fileDialogs.PickOpenFileAsync(
            new PresentationFileDialogOptions("JSON files (*.json)|*.json|All files (*.*)|*.*"),
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            await ViewModel.ImportFromFileAsync(filePath, cancellationToken);
        }
    }

    private async void ImportFromClipboardButton_OnClick(object sender, RoutedEventArgs e)
    {
        var cancellationToken = _activation.CurrentToken;
        var text = await _clipboard.GetTextAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            ViewModel.NotifyClipboardTextMissing();
            return;
        }

        await ViewModel.ImportJsonTextAsync(text, "剪贴板", cancellationToken);
    }

    private async void ExportDraftButton_OnClick(object sender, RoutedEventArgs e)
    {
        var cancellationToken = _activation.CurrentToken;
        var filePath = await _fileDialogs.PickSaveFileAsync(
            new PresentationFileDialogOptions(
                "JSON files (*.json)|*.json|All files (*.*)|*.*",
                "tts-rule.json"),
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            await ViewModel.ExportDraftToFileAsync(filePath, cancellationToken);
        }
    }
}
