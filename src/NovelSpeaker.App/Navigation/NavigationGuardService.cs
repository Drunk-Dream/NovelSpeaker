namespace NovelSpeaker.App.Navigation;

public sealed class NavigationGuardService : INavigationGuardService
{
    private readonly object _syncRoot = new();
    private Func<CancellationToken, Task<bool>>? _guard;

    public IDisposable Register(Func<CancellationToken, Task<bool>> guard)
    {
        ArgumentNullException.ThrowIfNull(guard);

        lock (_syncRoot)
        {
            _guard = guard;
        }

        return new Registration(this, guard);
    }

    public Task<bool> ConfirmNavigationAsync(CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<bool>>? guard;
        lock (_syncRoot)
        {
            guard = _guard;
        }

        return guard is null
            ? Task.FromResult(true)
            : guard(cancellationToken);
    }

    private void Unregister(Func<CancellationToken, Task<bool>> guard)
    {
        lock (_syncRoot)
        {
            if (ReferenceEquals(_guard, guard))
            {
                _guard = null;
            }
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly NavigationGuardService _owner;
        private readonly Func<CancellationToken, Task<bool>> _guard;
        private bool _disposed;

        public Registration(NavigationGuardService owner, Func<CancellationToken, Task<bool>> guard)
        {
            _owner = owner;
            _guard = guard;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.Unregister(_guard);
        }
    }
}
