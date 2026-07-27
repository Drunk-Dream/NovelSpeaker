namespace NovelSpeaker.App.Shell.Activation;

public sealed class PageActivationScope : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly PageActivationController _owner;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _cancellationToken;
    private readonly List<IDisposable> _registrations = [];
    private readonly HashSet<Task> _pendingOperations = [];
    private TaskCompletionSource? _operationsDrained;
    private bool _disposed;

    internal PageActivationScope(PageActivationController owner, long version)
    {
        _owner = owner;
        Version = version;
        _cancellationToken = _cancellation.Token;
    }

    public long Version { get; }

    public CancellationToken CancellationToken => _cancellationToken;

    public bool IsCurrent
    {
        get
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return false;
                }
            }

            return _owner.IsCurrent(this);
        }
    }

    internal int PendingOperationCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _pendingOperations.Count;
            }
        }
    }

    internal Task WaitForPendingOperationsAsync()
    {
        lock (_syncRoot)
        {
            return _pendingOperations.Count == 0
                ? Task.CompletedTask
                : _operationsDrained!.Task;
        }
    }

    /// <summary>
    /// Registers an event subscription, navigation guard, or other page-owned resource.
    /// Registrations are released only after page operations have been cancelled.
    /// </summary>
    public void Register(IDisposable registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (_syncRoot)
        {
            if (!_disposed)
            {
                _registrations.Add(registration);
                return;
            }
        }

        registration.Dispose();
    }

    public void Register(Action unregister)
    {
        ArgumentNullException.ThrowIfNull(unregister);
        Register(new ActionRegistration(unregister));
    }

    /// <summary>
    /// Registers a page-owned operation so its completion and exception are always observed.
    /// Failures from an activation that is no longer current are intentionally suppressed.
    /// </summary>
    internal void Register(Task operation, Action<Exception>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_syncRoot)
        {
            if (_pendingOperations.Count == 0)
            {
                _operationsDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _pendingOperations.Add(operation);
        }

        var awaiter = operation.ConfigureAwait(false).GetAwaiter();
        if (awaiter.IsCompleted)
        {
            CompleteOperation(operation, reportFailure);
            return;
        }

        awaiter.UnsafeOnCompleted(() => CompleteOperation(operation, reportFailure));
    }

    internal void Run(
        Func<CancellationToken, Task> operation,
        Action<Exception>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Task task;
        try
        {
            task = operation(CancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            if (IsCurrent)
            {
                reportFailure?.Invoke(exception);
            }

            return;
        }

        Register(task, reportFailure);
    }

    public bool TryCommit(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsCurrent)
        {
            return false;
        }

        action();
        return true;
    }

    public void Dispose()
    {
        _owner.Release(this);
        DisposeCore();
    }

    internal void DisposeCore()
    {
        IDisposable[] registrations;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            registrations = [.. _registrations];
            _registrations.Clear();
        }

        _cancellation.Cancel();

        for (var index = registrations.Length - 1; index >= 0; index--)
        {
            registrations[index].Dispose();
        }

        _cancellation.Dispose();
    }

    private void CompleteOperation(Task operation, Action<Exception>? reportFailure)
    {
        try
        {
            operation.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrent)
            {
                reportFailure?.Invoke(exception);
            }
        }
        finally
        {
            lock (_syncRoot)
            {
                _pendingOperations.Remove(operation);
                if (_pendingOperations.Count == 0)
                {
                    _operationsDrained?.TrySetResult();
                }
            }
        }
    }

    private sealed class ActionRegistration(Action unregister) : IDisposable
    {
        private Action? _unregister = unregister;

        public void Dispose()
        {
            Interlocked.Exchange(ref _unregister, null)?.Invoke();
        }
    }
}
