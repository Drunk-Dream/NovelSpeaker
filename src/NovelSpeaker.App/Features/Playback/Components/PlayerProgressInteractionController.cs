using System.Windows.Controls;
using System.Windows.Input;
using NovelSpeaker.App.Features.Playback.Presentation;

namespace NovelSpeaker.App.Features.Playback.Components;

internal sealed class PlayerProgressInteractionController
{
    private readonly Func<PlayerViewModel?> _getViewModel;
    private readonly Func<CancellationToken> _getActivationToken;
    private bool _isKeyboardAdjusting;

    public PlayerProgressInteractionController(
        Func<PlayerViewModel?> getViewModel,
        Func<CancellationToken> getActivationToken)
    {
        _getViewModel = getViewModel;
        _getActivationToken = getActivationToken;
    }

    public void Preview(double value)
    {
        var viewModel = _getViewModel();
        if (viewModel?.IsSegmentProgressDragging == true)
        {
            viewModel.PreviewSegmentProgress(value);
        }
    }

    public void BeginMouse(Slider slider)
    {
        var viewModel = _getViewModel();
        viewModel?.BeginSegmentProgressInteraction();
        viewModel?.PreviewSegmentProgress(slider.Value);
    }

    public Task CommitMouseAsync(Slider slider)
    {
        var viewModel = _getViewModel();
        return viewModel?.IsSegmentProgressDragging == true
            ? viewModel.CommitSegmentProgressAsync(slider.Value, _getActivationToken())
            : Task.CompletedTask;
    }

    public void BeginKeyboard(Key key)
    {
        var viewModel = _getViewModel();
        if (viewModel is null || !IsProgressKey(key) || _isKeyboardAdjusting)
        {
            return;
        }

        _isKeyboardAdjusting = true;
        viewModel.BeginSegmentProgressInteraction();
    }

    public Task CommitKeyboardAsync(Slider slider, Key key)
    {
        var viewModel = _getViewModel();
        if (viewModel is null || !_isKeyboardAdjusting || !IsProgressKey(key))
        {
            return Task.CompletedTask;
        }

        _isKeyboardAdjusting = false;
        return viewModel.CommitSegmentProgressAsync(slider.Value, _getActivationToken());
    }

    private static bool IsProgressKey(Key key)
    {
        return key is Key.Left or Key.Right or Key.Up or Key.Down or
            Key.PageUp or Key.PageDown or Key.Home or Key.End;
    }
}
