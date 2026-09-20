using System.Windows.Input;
using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.ImportTextSettings;

public partial class ImportTextSettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<ImportTextSettingsViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly PageEventOperationRunner _eventOperations;

    public ImportTextSettingsPage(
        ImportTextSettingsViewModel viewModel,
        PageEventOperationRunner eventOperations)
    {
        ViewModel = viewModel;
        _eventOperations = eventOperations;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public ImportTextSettingsViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        using var operation = _eventOperations.StartCriticalLoad(NovelSpeaker.Application.Observability.OperationCatalog.UiSettingsLoad);
        var activation = _activation.Activate();
        ViewModel.Activate(activation);
        activation.Register(ViewModel.Deactivate);
        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Succeeded());
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Cancelled());
        }
        catch
        {
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Failed("page-load-failed"));
            throw;
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        return Task.CompletedTask;
    }

    private async void BookFileNameTemplateTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await _eventOperations.RunAsync(
            _activation,
            "保存文件名模板失败",
            ViewModel.CommitBookFileNameTemplateAsync);
    }

    private async void BookFileNameTemplateTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await _eventOperations.RunAsync(
            _activation,
            "保存文件名模板失败",
            ViewModel.CommitBookFileNameTemplateAsync);
    }

    private async void LongParagraphThresholdTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await _eventOperations.RunAsync(
            _activation,
            "保存长段拆分阈值失败",
            ViewModel.CommitLongParagraphThresholdAsync);
    }

    private async void LongParagraphThresholdTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await _eventOperations.RunAsync(
            _activation,
            "保存长段拆分阈值失败",
            ViewModel.CommitLongParagraphThresholdAsync);
    }
}
