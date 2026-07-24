using System.Windows.Input;
using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.ImportTextSettings;

public partial class ImportTextSettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<ImportTextSettingsViewModel>
{
    private readonly PageActivationController _activation = new();

    public ImportTextSettingsPage(ImportTextSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public ImportTextSettingsViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        ViewModel.Activate(activation.CancellationToken);
        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        ViewModel.Deactivate();
        return Task.CompletedTask;
    }

    private async void BookFileNameTemplateTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.CommitBookFileNameTemplateAsync(_activation.CurrentToken);
    }

    private async void BookFileNameTemplateTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await ViewModel.CommitBookFileNameTemplateAsync(_activation.CurrentToken);
    }

    private async void LongParagraphThresholdTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.CommitLongParagraphThresholdAsync(_activation.CurrentToken);
    }

    private async void LongParagraphThresholdTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await ViewModel.CommitLongParagraphThresholdAsync(_activation.CurrentToken);
    }
}
