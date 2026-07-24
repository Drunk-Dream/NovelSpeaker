using System.Windows.Input;
using NovelSpeaker.App.Activation;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class PlaybackSettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<PlaybackSettingsViewModel>
{
    private readonly PageActivationController _activation = new();

    public PlaybackSettingsPage(PlaybackSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public PlaybackSettingsViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
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
        return Task.CompletedTask;
    }

    private async void DefaultSpeakSpeedTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.CommitDefaultSpeakSpeedAsync(_activation.CurrentToken);
    }

    private async void DefaultSpeakSpeedTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await ViewModel.CommitDefaultSpeakSpeedAsync(_activation.CurrentToken);
    }

    private async void PrefetchCountTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.CommitPrefetchCountAsync(_activation.CurrentToken);
    }

    private async void PrefetchCountTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await ViewModel.CommitPrefetchCountAsync(_activation.CurrentToken);
    }
}
