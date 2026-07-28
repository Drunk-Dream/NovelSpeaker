using System.Windows.Controls;
using System.Windows.Input;

namespace NovelSpeaker.App.Features.Playback.Components;

internal sealed class PlayerProgressInteractionController
{
    private readonly Func<ISegmentProgressInteractionTarget?> _getTarget;
    private readonly Func<CancellationToken> _getActivationToken;
    private bool _isKeyboardAdjusting;

    public PlayerProgressInteractionController(
        Func<ISegmentProgressInteractionTarget?> getTarget,
        Func<CancellationToken> getActivationToken)
    {
        _getTarget = getTarget;
        _getActivationToken = getActivationToken;
    }

    public void Preview(double value)
    {
        var target = _getTarget();
        if (target?.IsSegmentProgressDragging == true)
        {
            target.PreviewSegmentProgress(value);
        }
    }

    public void BeginMouse(Slider slider)
    {
        var target = _getTarget();
        target?.BeginSegmentProgressInteraction();
        target?.PreviewSegmentProgress(slider.Value);
        if (target?.IsSegmentProgressDragging == true)
        {
            OpenTooltip(slider);
        }
    }

    public void OnMouseEnter(Slider slider)
    {
        OpenTooltip(slider);
    }

    public void OnMouseLeave(Slider slider)
    {
        if (_getTarget()?.IsSegmentProgressDragging != true)
        {
            CloseTooltip(slider);
        }
    }

    public Task CommitMouseAsync(Slider slider)
    {
        CloseTooltip(slider);
        var target = _getTarget();
        return target?.IsSegmentProgressDragging == true
            ? target.CommitSegmentProgressAsync(slider.Value, _getActivationToken())
            : Task.CompletedTask;
    }

    public void BeginKeyboard(Slider slider, Key key)
    {
        var target = _getTarget();
        if (target is null || !IsProgressKey(key) || _isKeyboardAdjusting)
        {
            return;
        }

        _isKeyboardAdjusting = true;
        target.BeginSegmentProgressInteraction();
        if (target.IsSegmentProgressDragging)
        {
            OpenTooltip(slider);
        }
    }

    public Task CommitKeyboardAsync(Slider slider, Key key)
    {
        CloseTooltip(slider);
        var target = _getTarget();
        if (target is null || !_isKeyboardAdjusting || !IsProgressKey(key))
        {
            return Task.CompletedTask;
        }

        _isKeyboardAdjusting = false;
        return target.CommitSegmentProgressAsync(slider.Value, _getActivationToken());
    }

    private static bool IsProgressKey(Key key)
    {
        return key is Key.Left or Key.Right or Key.Up or Key.Down or
            Key.PageUp or Key.PageDown or Key.Home or Key.End;
    }

    private static void OpenTooltip(Slider slider)
    {
        if (slider.ToolTip is not ToolTip toolTip)
        {
            return;
        }

        toolTip.PlacementTarget = slider;
        toolTip.StaysOpen = true;
        toolTip.IsOpen = true;
    }

    private static void CloseTooltip(Slider slider)
    {
        if (slider.ToolTip is ToolTip toolTip)
        {
            toolTip.IsOpen = false;
        }
    }
}
