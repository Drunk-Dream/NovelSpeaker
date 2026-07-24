using System.Windows;
using System.Windows.Input;
using NovelSpeaker.App.Features.Playback.Presentation;

namespace NovelSpeaker.App.Features.Playback.Components;

internal sealed class PlayerSpeedCommitController
{
    private readonly Func<PlayerViewModel?> _getViewModel;
    private readonly Func<CancellationToken> _getActivationToken;

    public PlayerSpeedCommitController(
        Func<PlayerViewModel?> getViewModel,
        Func<CancellationToken> getActivationToken)
    {
        _getViewModel = getViewModel;
        _getActivationToken = getActivationToken;
    }

    public async Task<bool> CommitOnEnterAsync(Key key)
    {
        var viewModel = _getViewModel();
        if (key != Key.Enter || viewModel is null)
        {
            return false;
        }

        await viewModel.CommitSpeakSpeedAsync(_getActivationToken());
        return true;
    }

    public Task CommitOnLostFocusAsync(DependencyObject? newFocus)
    {
        var viewModel = _getViewModel();
        if (viewModel is null ||
            !viewModel.IsSpeedMenuOpen ||
            newFocus is FrameworkElement { Name: "DecreaseSpeedButton" or "IncreaseSpeedButton" })
        {
            return Task.CompletedTask;
        }

        return viewModel.CommitSpeakSpeedAsync(_getActivationToken());
    }
}
