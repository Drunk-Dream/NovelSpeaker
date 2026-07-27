using System.Collections.Concurrent;

namespace NovelSpeaker.TestKit.Common;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _syncRoot = new();
    private readonly HashSet<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset? initialUtcNow = null)
    {
        _utcNow = initialUtcNow ?? new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_syncRoot)
        {
            return _utcNow;
        }
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public int PendingTimerCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _timers.Count(static timer => timer.TryGetNextDue(out _));
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, dueTime, period);
        lock (_syncRoot)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        while (true)
        {
            List<ManualTimer> dueTimers;
            lock (_syncRoot)
            {
                var target = _utcNow + delta;
                var nextDue = _timers
                    .Where(timer => timer.TryGetNextDue(out var due) && due <= target)
                    .Select(timer => timer.GetNextDue())
                    .DefaultIfEmpty(DateTimeOffset.MaxValue)
                    .Min();

                if (nextDue == DateTimeOffset.MaxValue)
                {
                    _utcNow = target;
                    return;
                }

                delta = target - nextDue;
                _utcNow = nextDue;
                dueTimers = _timers
                    .Where(timer => timer.TryGetNextDue(out var due) && due == nextDue)
                    .ToList();
            }

            foreach (var timer in dueTimers)
            {
                timer.Fire();
            }
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _provider;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private bool _disposed;
        private TimeSpan _period;
        private DateTimeOffset? _nextDue;

        public ManualTimer(
            ManualTimeProvider provider,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _provider = provider;
            _callback = callback;
            _state = state;
            Change(dueTime, period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_provider._syncRoot)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _nextDue = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : _provider._utcNow + dueTime;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_provider._syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _nextDue = null;
                _provider._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public bool TryGetNextDue(out DateTimeOffset due)
        {
            lock (_provider._syncRoot)
            {
                due = _nextDue ?? DateTimeOffset.MaxValue;
                return !_disposed && _nextDue is not null;
            }
        }

        public DateTimeOffset GetNextDue()
        {
            lock (_provider._syncRoot)
            {
                return _nextDue ?? DateTimeOffset.MaxValue;
            }
        }

        public void Fire()
        {
            lock (_provider._syncRoot)
            {
                if (_disposed || _nextDue is null)
                {
                    return;
                }

                if (_period == Timeout.InfiniteTimeSpan)
                {
                    _nextDue = null;
                    _provider._timers.Remove(this);
                }
                else
                {
                    _nextDue = _provider._utcNow + _period;
                }
            }

            _callback(_state);
        }
    }
}
