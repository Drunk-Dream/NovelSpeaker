namespace NovelSpeaker.App.Player;

public sealed class PlayerAutoScrollCoordinator : IPlayerAutoScrollCoordinator, IDisposable
{
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromSeconds(4);

    private readonly TimeProvider _timeProvider;
    private readonly object _syncRoot = new();

    private ITimer? _restoreTimer;
    private int _programmaticScrollDepth;
    private AutoScrollMode _mode = AutoScrollMode.AutoCentering;
    private int _pendingRestoreVersion;

    public PlayerAutoScrollCoordinator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public bool ShouldAutoCenter
    {
        get
        {
            lock (_syncRoot)
            {
                return _mode == AutoScrollMode.AutoCentering;
            }
        }
    }

    public bool ShowReturnToCurrentSegment
    {
        get
        {
            lock (_syncRoot)
            {
                return _mode != AutoScrollMode.AutoCentering;
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
        var shouldRaise = false;

        lock (_syncRoot)
        {
            if (_programmaticScrollDepth > 0 || _mode == AutoScrollMode.ScrollbarDragging)
            {
                return;
            }

            shouldRaise = _mode != AutoScrollMode.ManualBrowsing;
            _mode = AutoScrollMode.ManualBrowsing;
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
            shouldRaise = _mode != AutoScrollMode.ScrollbarDragging;
            _mode = AutoScrollMode.ScrollbarDragging;
        }

        if (shouldRaise)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void EndScrollbarDrag()
    {
        lock (_syncRoot)
        {
            _mode = AutoScrollMode.ManualBrowsing;
            ScheduleRestore_NoLock();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
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

    public void ReturnToCurrentSegment()
    {
        var shouldRaise = false;

        lock (_syncRoot)
        {
            CancelRestore_NoLock();
            shouldRaise = _mode != AutoScrollMode.AutoCentering;
            _mode = AutoScrollMode.AutoCentering;
        }

        if (shouldRaise)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ResetForChapterChange()
    {
        Reset();
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
            shouldRaise = _mode != AutoScrollMode.AutoCentering || _programmaticScrollDepth != 0;
            _mode = AutoScrollMode.AutoCentering;
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
            if (_pendingRestoreVersion != version || _mode == AutoScrollMode.ScrollbarDragging)
            {
                return;
            }

            CancelRestore_NoLock();
            shouldRaise = _mode != AutoScrollMode.AutoCentering;
            _mode = AutoScrollMode.AutoCentering;
        }

        if (shouldRaise)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private enum AutoScrollMode
    {
        AutoCentering,
        ManualBrowsing,
        ScrollbarDragging
    }
}
