namespace NovelSpeaker.App.Player;

public sealed class PlayerAutoScrollCoordinator : IPlayerAutoScrollCoordinator, IDisposable
{
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromSeconds(4);

    private readonly TimeProvider _timeProvider;
    private readonly object _syncRoot = new();

    private ITimer? _restoreTimer;
    private int _programmaticScrollDepth;
    private PlayerAutoScrollState _state = PlayerAutoScrollState.AutoCentering;
    private int _pendingRestoreVersion;

    public PlayerAutoScrollCoordinator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public PlayerAutoScrollState State
    {
        get
        {
            lock (_syncRoot)
            {
                return _state;
            }
        }
    }

    public bool ShouldAutoCenter
    {
        get
        {
            lock (_syncRoot)
            {
                return _state == PlayerAutoScrollState.AutoCentering;
            }
        }
    }

    public bool ShowReturnToCurrentSegment
    {
        get
        {
            lock (_syncRoot)
            {
                return _state != PlayerAutoScrollState.AutoCentering;
            }
        }
    }

    public int PendingRestoreVersion
    {
        get
        {
            lock (_syncRoot)
            {
                return _pendingRestoreVersion;
            }
        }
    }

    public event EventHandler? StateChanged;

    public void NotifyUserScrollInput()
    {
        NotifyScrollInput(ignoreProgrammaticScroll: false);
    }

    public void NotifyPassiveScrollChange()
    {
        NotifyScrollInput(ignoreProgrammaticScroll: true);
    }

    private void NotifyScrollInput(bool ignoreProgrammaticScroll)
    {
        var shouldRaise = false;

        lock (_syncRoot)
        {
            if ((ignoreProgrammaticScroll && _programmaticScrollDepth > 0) ||
                _state == PlayerAutoScrollState.ScrollbarDragging)
            {
                return;
            }

            shouldRaise = _state != PlayerAutoScrollState.ManualBrowsing;
            _state = PlayerAutoScrollState.ManualBrowsing;
            ScheduleRestore_NoLock();
        }

        if (shouldRaise)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void BeginScrollbarDrag()
    {
        var shouldRaise = false;

        lock (_syncRoot)
        {
            CancelRestore_NoLock();
            shouldRaise = _state != PlayerAutoScrollState.ScrollbarDragging;
            _state = PlayerAutoScrollState.ScrollbarDragging;
        }

        if (shouldRaise)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void EndScrollbarDrag()
    {
        var shouldRaise = false;

        lock (_syncRoot)
        {
            shouldRaise = _state != PlayerAutoScrollState.ManualBrowsing;
            _state = PlayerAutoScrollState.ManualBrowsing;
            ScheduleRestore_NoLock();
        }

        if (shouldRaise)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void BeginProgrammaticScroll()
    {
        lock (_syncRoot)
        {
            _programmaticScrollDepth++;
        }
    }

    public void EndProgrammaticScroll()
    {
        lock (_syncRoot)
        {
            if (_programmaticScrollDepth > 0)
            {
                _programmaticScrollDepth--;
            }
        }
    }

    public void ResumeAutoCenter()
    {
        var shouldRaise = false;

        lock (_syncRoot)
        {
            CancelRestore_NoLock();
            shouldRaise = _state != PlayerAutoScrollState.AutoCentering;
            _state = PlayerAutoScrollState.AutoCentering;
        }

        if (shouldRaise)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ResetForPageLeave()
    {
        Reset();
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            CancelRestore_NoLock();
        }
    }

    private void Reset()
    {
        var shouldRaise = false;

        lock (_syncRoot)
        {
            CancelRestore_NoLock();
            shouldRaise = _state != PlayerAutoScrollState.AutoCentering || _programmaticScrollDepth != 0;
            _state = PlayerAutoScrollState.AutoCentering;
            _programmaticScrollDepth = 0;
        }

        if (shouldRaise)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ScheduleRestore_NoLock()
    {
        CancelRestore_NoLock();
        _pendingRestoreVersion++;
        var version = _pendingRestoreVersion;
        _restoreTimer = _timeProvider.CreateTimer(
            _ => RestoreAutoCenter(version),
            null,
            RestoreDelay,
            Timeout.InfiniteTimeSpan);
    }

    private void CancelRestore_NoLock()
    {
        _restoreTimer?.Dispose();
        _restoreTimer = null;
    }

    private void RestoreAutoCenter(int version)
    {
        var shouldRaise = false;

        lock (_syncRoot)
        {
            if (_pendingRestoreVersion != version || _state == PlayerAutoScrollState.ScrollbarDragging)
            {
                return;
            }

            CancelRestore_NoLock();
            shouldRaise = _state != PlayerAutoScrollState.AutoCentering;
            _state = PlayerAutoScrollState.AutoCentering;
        }

        if (shouldRaise)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

}
