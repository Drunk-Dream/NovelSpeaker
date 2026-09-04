namespace NovelSpeaker.App.Shell.Input;

public sealed class KeyboardShortcutTargetRegistry : IKeyboardShortcutTargetRegistry
{
    private readonly object _syncRoot = new();
    private IKeyboardShortcutTarget? _current;

    public IKeyboardShortcutTarget? Current
    {
        get
        {
            lock (_syncRoot)
            {
                return _current;
            }
        }
    }

    public IDisposable Register(IKeyboardShortcutTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_syncRoot)
        {
            _current = target;
        }

        return new Registration(this, target);
    }

    private void Unregister(IKeyboardShortcutTarget target)
    {
        lock (_syncRoot)
        {
            if (ReferenceEquals(_current, target))
            {
                _current = null;
            }
        }
    }

    private sealed class Registration(
        KeyboardShortcutTargetRegistry owner,
        IKeyboardShortcutTarget target) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Unregister(target);
            }
        }
    }
}
