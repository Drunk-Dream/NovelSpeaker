namespace NovelSpeaker.App.Shared.Presentation.Scrolling;

/// <summary>
/// Owns whether a current-item locator should be offered after explicit user browsing.
/// Visual-tree visibility measurement and scrolling remain platform responsibilities.
/// </summary>
internal sealed class CurrentItemLocatorController
{
    private bool _isUserBrowsing;
    private bool _isContinuousUserScroll;
    private bool _isLocating;
    private bool _isCurrentItemVisible;

    public bool IsLocatorVisible { get; private set; }

    public event EventHandler? StateChanged;

    public void NotifyUserScrollInput()
    {
        _isUserBrowsing = true;
        _isLocating = false;
    }

    public void NotifyCurrentItemChanged()
    {
        _isUserBrowsing = false;
        _isContinuousUserScroll = false;
        _isLocating = false;
        _isCurrentItemVisible = false;
        SetLocatorVisible(false);
    }

    public void BeginContinuousUserScroll()
    {
        _isContinuousUserScroll = true;
        NotifyUserScrollInput();
    }

    public void EndContinuousUserScroll()
    {
        _isContinuousUserScroll = false;
        _isUserBrowsing = false;
        if (_isCurrentItemVisible)
        {
            SetLocatorVisible(false);
        }
    }

    public void ObserveCurrentItem(bool hasCurrentItem, bool isVisible)
    {
        _isCurrentItemVisible = hasCurrentItem && isVisible;
        if (!hasCurrentItem)
        {
            _isUserBrowsing = false;
            _isContinuousUserScroll = false;
            _isLocating = false;
            SetLocatorVisible(false);
            return;
        }

        if (_isLocating)
        {
            if (isVisible)
            {
                _isLocating = false;
                SetLocatorVisible(false);
            }

            return;
        }

        if (!_isUserBrowsing)
        {
            return;
        }

        SetLocatorVisible(!isVisible);
        if (isVisible && !_isContinuousUserScroll)
        {
            _isUserBrowsing = false;
        }
    }

    public bool TryBeginLocate()
    {
        if (!IsLocatorVisible)
        {
            return false;
        }

        _isUserBrowsing = false;
        _isContinuousUserScroll = false;
        _isLocating = true;
        return true;
    }

    private void SetLocatorVisible(bool value)
    {
        if (IsLocatorVisible == value)
        {
            return;
        }

        IsLocatorVisible = value;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
