namespace NovelSpeaker.App.Player;

public sealed class PlayerLayoutController : IPlayerLayoutController
{
    public const double CompactBreakpoint = 1080d;

    private double _currentWidth = double.PositiveInfinity;
    private bool _isCompactLayout;
    private bool _isDrawerOpen;

    public bool IsCompactLayout => _isCompactLayout;

    public bool IsDrawerOpen => _isDrawerOpen;

    public event EventHandler? StateChanged;

    public void UpdateWidth(double width)
    {
        _currentWidth = width;

        var nextIsCompact = _currentWidth < CompactBreakpoint;
        var hasChanged = false;

        if (_isCompactLayout != nextIsCompact)
        {
            _isCompactLayout = nextIsCompact;
            hasChanged = true;
        }

        if (!_isCompactLayout && _isDrawerOpen)
        {
            _isDrawerOpen = false;
            hasChanged = true;
        }

        if (hasChanged)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OpenDrawer()
    {
        if (!_isCompactLayout || _isDrawerOpen)
        {
            return;
        }

        _isDrawerOpen = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CloseDrawer()
    {
        if (!_isDrawerOpen)
        {
            return;
        }

        _isDrawerOpen = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleDrawer()
    {
        if (!_isCompactLayout)
        {
            return;
        }

        if (_isDrawerOpen)
        {
            CloseDrawer();
        }
        else
        {
            OpenDrawer();
        }
    }
}
