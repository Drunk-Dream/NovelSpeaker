using System;

namespace NovelSpeaker.App.Shell;

public sealed class ShellLayoutController : IShellLayoutController
{
    public const double CollapseBreakpoint = 1080d;

    private double _currentWidth = double.PositiveInfinity;
    private bool _isPaneOpen = true;
    private bool _userPrefersPaneOpen = true;

    public bool IsPaneOpen => _isPaneOpen;

    public event EventHandler<bool>? PaneStateChanged;

    public void UpdateWindowWidth(double width)
    {
        _currentWidth = width;
        ApplyDesiredState();
    }

    public void HandlePaneStateChanged(bool isPaneOpen)
    {
        if (_currentWidth >= CollapseBreakpoint)
        {
            _userPrefersPaneOpen = isPaneOpen;
        }

        ApplyDesiredState();
    }

    private void ApplyDesiredState()
    {
        var desiredState = _currentWidth < CollapseBreakpoint
            ? false
            : _userPrefersPaneOpen;

        if (_isPaneOpen == desiredState)
        {
            return;
        }

        _isPaneOpen = desiredState;
        PaneStateChanged?.Invoke(this, _isPaneOpen);
    }
}
