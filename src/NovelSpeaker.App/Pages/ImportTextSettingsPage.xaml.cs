using System.Windows.Input;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class ImportTextSettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<ImportTextSettingsViewModel>
{
    public ImportTextSettingsPage(ImportTextSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public ImportTextSettingsViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        return ViewModel.LoadAsync(CancellationToken.None);
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }

    private async void BookFileNameTemplateTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.CommitBookFileNameTemplateAsync(CancellationToken.None);
    }

    private async void BookFileNameTemplateTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await ViewModel.CommitBookFileNameTemplateAsync(CancellationToken.None);
    }

    private async void LongParagraphThresholdTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.CommitLongParagraphThresholdAsync(CancellationToken.None);
    }

    private async void LongParagraphThresholdTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await ViewModel.CommitLongParagraphThresholdAsync(CancellationToken.None);
    }
}
