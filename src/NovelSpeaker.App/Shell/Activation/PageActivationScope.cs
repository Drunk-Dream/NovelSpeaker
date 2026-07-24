namespace NovelSpeaker.App.Shell.Activation;

public sealed class PageActivationScope : IDisposable
{
    private readonly PageActivationController _owner;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _cancellationToken;
    private readonly List<IDisposable> _registrations = [];
    private bool _disposed;

    internal PageActivationScope(PageActivationController owner, long version)
    {
        _owner = owner;
        Version = version;
        _cancellationToken = _cancellation.Token;
    }

    public long Version { get; }

    public CancellationToken CancellationToken => _cancellationToken;

    public bool IsCurrent => !_disposed && _owner.IsCurrent(this);

    /// <summary>
    /// Registers an event subscription, navigation guard, or other page-owned resource.
    /// Registrations are released only after page operations have been cancelled.
    /// </summary>
    public void Register(IDisposable registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (_disposed)
        {
            registration.Dispose();
            return;
        }

        _registrations.Add(registration);
    }

    public void Register(Action unregister)
    {
        ArgumentNullException.ThrowIfNull(unregister);
        Register(new ActionRegistration(unregister));
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();

        for (var index = _registrations.Count - 1; index >= 0; index--)
        {
            _registrations[index].Dispose();
        }

        _registrations.Clear();
        _cancellation.Dispose();
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
