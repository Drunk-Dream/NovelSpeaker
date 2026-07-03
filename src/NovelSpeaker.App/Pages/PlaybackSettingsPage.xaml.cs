using System.Windows.Input;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class PlaybackSettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<PlaybackSettingsViewModel>
{
    public PlaybackSettingsPage(PlaybackSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public PlaybackSettingsViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        return ViewModel.LoadAsync(CancellationToken.None);
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }

    private async void DefaultSpeakSpeedTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.CommitDefaultSpeakSpeedAsync(CancellationToken.None);
    }

    private async void DefaultSpeakSpeedTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await ViewModel.CommitDefaultSpeakSpeedAsync(CancellationToken.None);
    }

    private async void PrefetchCountTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.CommitPrefetchCountAsync(CancellationToken.None);
    }

    private async void PrefetchCountTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await ViewModel.CommitPrefetchCountAsync(CancellationToken.None);
    }
}
