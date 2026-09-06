using System.Collections.Concurrent;
using System.Threading.Channels;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Owns the serialized command boundary for the playback session and its device events.
/// </summary>
internal sealed class PlaybackCommandProcessor : IAsyncDisposable
{
    private readonly Func<PlaybackEventCommand, CancellationToken, Task> _eventHandler;
    private readonly Action _eventFailureHandler;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly Channel<PlaybackEventCommand> _eventCommands =
        Channel.CreateUnbounded<PlaybackEventCommand>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                AllowSynchronousContinuations = false
            });
    private readonly ConcurrentDictionary<PlaybackEventKey, byte> _pendingEventCommands = new();
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly CancellationTokenSource _eventCancellation = new();
    private readonly Task _eventProcessor;
    private readonly object _disposeGate = new();

    private bool _accepting = true;
    private bool _disposed;
    private Task? _disposeTask;
    private long _eventEpoch;

    public PlaybackCommandProcessor(
        Func<PlaybackEventCommand, CancellationToken, Task> eventHandler,
        Action eventFailureHandler)
    {
        _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
        _eventFailureHandler = eventFailureHandler ?? throw new ArgumentNullException(nameof(eventFailureHandler));
        _eventProcessor = ProcessEventsAsync();
    }

    public CancellationToken LifecycleToken => _lifecycleCancellation.Token;

    public long CurrentEventEpoch => Volatile.Read(ref _eventEpoch);

    public Task RunSerializedAsync(
        Func<CancellationToken, Task> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return RunSerializedCoreAsync(command, cancellationToken);
    }

    public long AdvanceEventEpoch() => Interlocked.Increment(ref _eventEpoch);

    public void Enqueue(PlaybackEventCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_accepting || command.SessionId is null)
        {
            return;
        }

        if (!_pendingEventCommands.TryAdd(command.Key, 0))
        {
            return;
        }

        if (!_eventCommands.Writer.TryWrite(command))
        {
            _pendingEventCommands.TryRemove(command.Key, out _);
        }
    }

    public void BeginShutdown()
    {
        if (!_accepting)
        {
            return;
        }

        _accepting = false;
        _lifecycleCancellation.Cancel();
        _eventCancellation.Cancel();
        _eventCommands.Writer.TryComplete();
    }

    public async Task WaitForIdleAsync()
    {
        await _commandGate.WaitAsync().ConfigureAwait(false);
        _commandGate.Release();
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task RunSerializedCoreAsync(
        Func<CancellationToken, Task> command,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifecycleCancellation.Token);
        await _commandGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await command(linkedCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task ProcessEventsAsync()
    {
        try
        {
            await foreach (var command in _eventCommands.Reader
                               .ReadAllAsync(_eventCancellation.Token)
                               .ConfigureAwait(false))
            {
                try
                {
                    await _commandGate.WaitAsync(_lifecycleCancellation.Token).ConfigureAwait(false);
                    try
                    {
                        if (!_disposed)
                        {
                            await _eventHandler(command, _lifecycleCancellation.Token).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        _commandGate.Release();
                    }
                }
                catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
                {
                    // Closing cancels the owned event processor.
                }
                catch
                {
                    try
                    {
                        _eventFailureHandler();
                    }
                    catch
                    {
                        // Failure projection is outside this processor's ownership boundary.
                    }
                }
                finally
                {
                    _pendingEventCommands.TryRemove(command.Key, out _);
                }
            }
        }
        catch (OperationCanceledException) when (_eventCancellation.IsCancellationRequested)
        {
            // Closing cancels the owned event processor.
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        BeginShutdown();
        try
        {
            await _eventProcessor.ConfigureAwait(false);
        }
        finally
        {
            await WaitForIdleAsync().ConfigureAwait(false);
            _eventCancellation.Dispose();
            _lifecycleCancellation.Dispose();
            _commandGate.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed || !_accepting, this);
    }
}

internal enum PlaybackEventCommandKind
{
    Completed,
    Failed,
    SnapshotChanged
}

internal sealed record PlaybackEventCommand(
    PlaybackEventCommandKind Kind,
    Guid? SessionId,
    LocalAudioPlaybackSnapshot? Snapshot,
    PlaybackErrorEventArgs? Error,
    long EventEpoch)
{
    public PlaybackEventKey Key => new(
        EventEpoch,
        Kind,
        SessionId ?? Guid.Empty,
        Snapshot?.BookId,
        Snapshot?.ChapterIndex ?? -1,
        Snapshot?.SegmentIndex ?? -1,
        Snapshot?.State ?? PlaybackState.Idle,
        Snapshot?.PositionMilliseconds ?? 0,
        Snapshot?.DurationMilliseconds ?? 0,
        Error?.Kind ?? PlaybackErrorKind.Unknown);
}

internal readonly record struct PlaybackEventKey(
    long EventEpoch,
    PlaybackEventCommandKind Kind,
    Guid SessionId,
    string? BookId,
    int ChapterIndex,
    int SegmentIndex,
    PlaybackState State,
    long PositionMilliseconds,
    long DurationMilliseconds,
    PlaybackErrorKind ErrorKind);
