using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Input;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Playback.Presentation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Playback;

public partial class PlayerPage : System.Windows.Controls.Page, INavigationAware, INavigableView<PlayerViewModel>, IKeyboardShortcutTarget
{
    private readonly PageActivationController _activation = new();
    private readonly IKeyboardShortcutTargetRegistry? _shortcutTargets;

    public PlayerPage(
        PlayerViewModel viewModel,
        IKeyboardShortcutTargetRegistry? shortcutTargets = null)
    {
        ViewModel = viewModel;
        _shortcutTargets = shortcutTargets;
        InitializeComponent();
        PlayerView.DataContext = ViewModel;
    }

    public PlayerViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        PlayerView.ActivationToken = activation.CancellationToken;
        ViewModel.OnPageNavigatedTo(activation.CancellationToken);
        activation.Register(ViewModel.OnPageNavigatedFrom);
        if (_shortcutTargets is not null)
        {
            activation.Register(_shortcutTargets.Register(this));
        }
        var request = DataContext as PlayerRoute;

        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
            if (request is not null && activation.IsCurrent)
            {
                await ViewModel.HandleNavigationAsync(request, activation.CancellationToken);
            }
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        PlayerView.ActivationToken = new CancellationToken(canceled: true);
        return Task.CompletedTask;
    }

    public async Task<bool> HandleKeyboardShortcutAsync(
        KeyboardShortcutAction action,
        string? argument,
        CancellationToken cancellationToken)
    {
        if (argument is not null || _activation.Current is not { IsCurrent: true } activation)
        {
            return false;
        }

        var command = action switch
        {
            KeyboardShortcutAction.TogglePlayback => ViewModel.TogglePlayPauseCommand,
            KeyboardShortcutAction.PreviousSegment => ViewModel.PreviousSegmentCommand,
            KeyboardShortcutAction.NextSegment => ViewModel.NextSegmentCommand,
            KeyboardShortcutAction.PreviousChapter => ViewModel.PreviousChapterCommand,
            KeyboardShortcutAction.NextChapter => ViewModel.NextChapterCommand,
            _ => null
        };
        if (command is null || !command.CanExecute(null))
        {
            return false;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            activation.CancellationToken);
        try
        {
            await command.ExecuteAsync(null).WaitAsync(linkedCancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (activation.CancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return true;
    }
}
