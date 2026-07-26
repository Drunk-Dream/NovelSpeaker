using System.Windows.Input;
using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.PlaybackSettings;

public partial class PlaybackSettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<PlaybackSettingsViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly PageEventOperationRunner _eventOperations;

    public PlaybackSettingsPage(
        PlaybackSettingsViewModel viewModel,
        PageEventOperationRunner eventOperations)
    {
        ViewModel = viewModel;
        _eventOperations = eventOperations;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public PlaybackSettingsViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        ViewModel.Activate(activation);
        activation.Register(ViewModel.Deactivate);
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
        await _eventOperations.RunAsync(
            _activation,
            "更新默认语速失败",
            ViewModel.CommitDefaultSpeakSpeedAsync);
    }

    private async void DefaultSpeakSpeedTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await _eventOperations.RunAsync(
            _activation,
            "更新默认语速失败",
            ViewModel.CommitDefaultSpeakSpeedAsync);
    }

    private async void PrefetchCountTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await _eventOperations.RunAsync(
            _activation,
            "保存预取段落数量失败",
            ViewModel.CommitPrefetchCountAsync);
    }

    private async void PrefetchCountTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await _eventOperations.RunAsync(
            _activation,
            "保存预取段落数量失败",
            ViewModel.CommitPrefetchCountAsync);
    }
}
