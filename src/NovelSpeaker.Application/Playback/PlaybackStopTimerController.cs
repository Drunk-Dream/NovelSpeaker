namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Owns one replaceable playback stop request. Duration work is tracked and drained;
/// playback boundaries are consumed synchronously by the serialized session owner.
/// </summary>
internal sealed class PlaybackStopTimerController : IPlaybackStopTimer, IAsyncDisposable
{
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromDays(1);

    private readonly object _syncRoot = new();
    private readonly TimeProvider _timeProvider;
    private readonly Func<CancellationToken, Task> _pausePlayback;
    private readonly Action _reportFailure;
    private readonly HashSet<Task> _ownedTasks = [];

    private PlaybackStopTimerSnapshot _currentSnapshot = PlaybackStopTimerSnapshot.None;
    private CancellationTokenSource? _durationCancellation;
    private long _generation;
    private bool _disposed;

    public PlaybackStopTimerController(
        TimeProvider timeProvider,
        Func<CancellationToken, Task> pausePlayback,
        Action reportFailure)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _pausePlayback = pausePlayback ?? throw new ArgumentNullException(nameof(pausePlayback));
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    public PlaybackStopTimerSnapshot CurrentSnapshot
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentSnapshot;
            }
        }
    }

    public event EventHandler<PlaybackStopTimerSnapshot>? SnapshotChanged;

    public void ScheduleAfter(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1) || duration > MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "定时停止时长必须在 1 分钟到 24 小时之间。");
        }

        CancellationTokenSource? previousCancellation;
        CancellationTokenSource cancellation;
        PlaybackStopTimerSnapshot snapshot;
        long generation;
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            previousCancellation = _durationCancellation;
            cancellation = new CancellationTokenSource();
            _durationCancellation = cancellation;
            generation = ++_generation;
            snapshot = new PlaybackStopTimerSnapshot(
                PlaybackStopTimerMode.Duration,
                _timeProvider.GetUtcNow() + duration,
                duration,
                generation);
            _currentSnapshot = snapshot;
        }

        CancelAndDispose(previousCancellation);
        Track(RunDurationAsync(duration, generation, cancellation));
        SnapshotChanged?.Invoke(this, snapshot);
    }

    public void ScheduleAtEndOfSegment() =>
        ScheduleBoundary(PlaybackStopTimerMode.EndOfSegment);

    public void ScheduleAtEndOfChapter() =>
        ScheduleBoundary(PlaybackStopTimerMode.EndOfChapter);

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        PlaybackStopTimerSnapshot snapshot;
        var changed = false;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            cancellation = _durationCancellation;
            _durationCancellation = null;
            var version = ++_generation;
            changed = _currentSnapshot.IsActive;
            snapshot = new PlaybackStopTimerSnapshot(
                PlaybackStopTimerMode.None,
                null,
                null,
                version);
            _currentSnapshot = snapshot;
        }

        CancelAndDispose(cancellation);
        if (changed)
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    public bool TryConsumeBoundary(bool chapterEnded)
    {
        CancellationTokenSource? cancellation;
        PlaybackStopTimerSnapshot snapshot;
        lock (_syncRoot)
        {
            if (_disposed ||
                (_currentSnapshot.Mode != PlaybackStopTimerMode.EndOfSegment &&
                 !(_currentSnapshot.Mode == PlaybackStopTimerMode.EndOfChapter && chapterEnded)))
            {
                return false;
            }

            cancellation = _durationCancellation;
            _durationCancellation = null;
            var version = ++_generation;
            snapshot = new PlaybackStopTimerSnapshot(
                PlaybackStopTimerMode.None,
                null,
                null,
                version);
            _currentSnapshot = snapshot;
        }

        CancelAndDispose(cancellation);
        SnapshotChanged?.Invoke(this, snapshot);
        return true;
    }

    internal Task WaitForPendingOperationAsync()
    {
        lock (_syncRoot)
        {
            return _ownedTasks.Count == 0
                ? Task.CompletedTask
                : DrainTasksAsync(_ownedTasks.ToArray());
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cancellation;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _durationCancellation;
            _durationCancellation = null;
            var version = ++_generation;
            _currentSnapshot = new PlaybackStopTimerSnapshot(
                PlaybackStopTimerMode.None,
                null,
                null,
                version);
        }

        CancelAndDispose(cancellation);
        await WaitForPendingOperationAsync().ConfigureAwait(false);
    }

    private void ScheduleBoundary(PlaybackStopTimerMode mode)
    {
        CancellationTokenSource? previousCancellation;
        PlaybackStopTimerSnapshot snapshot;
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            previousCancellation = _durationCancellation;
            _durationCancellation = null;
            var version = ++_generation;
            snapshot = new PlaybackStopTimerSnapshot(mode, null, null, version);
            _currentSnapshot = snapshot;
        }

        CancelAndDispose(previousCancellation);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private async Task RunDurationAsync(
        TimeSpan duration,
        long generation,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            await Task.Delay(duration, _timeProvider, cancellationToken).ConfigureAwait(false);

            PlaybackStopTimerSnapshot expiredSnapshot;
            lock (_syncRoot)
            {
                if (_disposed ||
                    generation != _generation ||
                    _currentSnapshot.Mode != PlaybackStopTimerMode.Duration)
                {
                    return;
                }

                var version = ++_generation;
                expiredSnapshot = new PlaybackStopTimerSnapshot(
                    PlaybackStopTimerMode.None,
                    null,
                    null,
                    version);
                _currentSnapshot = expiredSnapshot;
            }

            SnapshotChanged?.Invoke(this, expiredSnapshot);
            await _pausePlayback(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Replacing, cancelling, or disposing the owned timer is normal control flow.
        }
        catch (Exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                _reportFailure();
            }
            catch
            {
                // Failure projection is outside this task's ownership boundary.
                // The owned duration task must still complete without becoming unobserved.
            }
        }
        finally
        {
            lock (_syncRoot)
            {
                if (ReferenceEquals(_durationCancellation, cancellation))
                {
                    _durationCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void Track(Task task)
    {
        lock (_syncRoot)
        {
            _ownedTasks.Add(task);
        }

        // Ownership bookkeeping is final cleanup and must run even after timer cancellation.
        task.ContinueWith(
            static (completedTask, state) =>
            {
                var owner = (PlaybackStopTimerController)state!;
                _ = completedTask.Exception;
                lock (owner._syncRoot)
                {
                    owner._ownedTasks.Remove(completedTask);
                }
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async Task DrainTasksAsync(Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // Completion is observed by Track; disposal only drains owned work.
        }
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
