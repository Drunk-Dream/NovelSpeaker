using NovelSpeaker.App.Shell.Activation;

namespace NovelSpeaker.App.Shared.Presentation;

/// <summary>
/// Owns one latest-wins operation. Callers commit on their owner thread; this is
/// cancellation/currentness ownership, not a scheduler or a data revision.
/// </summary>
internal sealed class LatestOperationSlot : IDisposable
{
    private readonly object _syncRoot = new();
    private long _identity;
    private Operation? _current;

    public Operation? Current => Volatile.Read(ref _current);

    public long Identity => Volatile.Read(ref _identity);

    public Operation Begin(CancellationToken cancellationToken = default, PageActivationScope? activation = null)
    {
        Operation? previous;
        Operation operation;
        lock (_syncRoot)
        {
            previous = _current;
            operation = new Operation(this, ++_identity, cancellationToken, activation);
            _current = operation;
        }

        previous?.CancelAndDispose();
        return operation;
    }

    public void Cancel()
    {
        Operation? operation;
        lock (_syncRoot)
        {
            operation = _current;
            _current = null;
            ++_identity;
        }

        operation?.CancelAndDispose();
    }

    public void Dispose() => Cancel();

    private void Release(Operation operation)
    {
        lock (_syncRoot)
        {
            if (ReferenceEquals(_current, operation)) _current = null;
        }
    }

    internal sealed class Operation : IDisposable
    {
        private readonly LatestOperationSlot _owner;
        private readonly PageActivationScope? _activation;
        private readonly object _syncRoot = new();
        private readonly CancellationTokenSource _cancellation;
        private bool _disposed;

        internal Operation(LatestOperationSlot owner, long identity, CancellationToken token, PageActivationScope? activation)
        {
            _owner = owner;
            _activation = activation;
            Identity = identity;
            _cancellation = activation is null
                ? CancellationTokenSource.CreateLinkedTokenSource(token)
                : CancellationTokenSource.CreateLinkedTokenSource(token, activation.CancellationToken);
            CancellationToken = _cancellation.Token;
        }

        public long Identity { get; }

        public CancellationToken CancellationToken { get; }

        public bool IsCurrent => ReferenceEquals(_owner.Current, this) &&
            !CancellationToken.IsCancellationRequested && (_activation?.IsCurrent ?? true);

        public bool TryCommit(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (!IsCurrent) return false;
            action();
            return true;
        }

        /// <summary>Observes completion on the caller's context and releases this operation once.</summary>
        public async Task RunAsync(Func<Operation, Task> action, Action<Exception>? reportFailure = null)
        {
            try
            {
                if (IsCurrent) await action(this).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (IsCurrent) reportFailure?.Invoke(exception);
            }
            finally
            {
                Dispose();
            }
        }

        internal void CancelAndDispose()
        {
            lock (_syncRoot)
            {
                if (_disposed) return;
                _disposed = true;
                try { _cancellation.Cancel(); }
                finally { _cancellation.Dispose(); }
            }
        }

        public void Dispose()
        {
            _owner.Release(this);
            lock (_syncRoot)
            {
                if (_disposed) return;
                _disposed = true;
                _cancellation.Dispose();
            }
        }
    }
}
