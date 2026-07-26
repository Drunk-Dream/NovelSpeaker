namespace NovelSpeaker.App.Shell.Activation;

/// <summary>
/// Owns the single current activation of a navigation page.
/// </summary>
public sealed class PageActivationController : IDisposable
{
    private long _nextVersion;
    private PageActivationScope? _current;

    public PageActivationScope Activate()
    {
        Deactivate();
        var scope = new PageActivationScope(this, Interlocked.Increment(ref _nextVersion));
        _current = scope;
        return scope;
    }

    public PageActivationScope? Current => Volatile.Read(ref _current);

    public void Deactivate()
    {
        var scope = Interlocked.Exchange(ref _current, null);
        scope?.DisposeCore();
    }

    public void Dispose()
    {
        Deactivate();
    }

    internal bool IsCurrent(PageActivationScope scope)
    {
        return ReferenceEquals(Volatile.Read(ref _current), scope) &&
               !scope.CancellationToken.IsCancellationRequested;
    }

    internal void Release(PageActivationScope scope)
    {
        Interlocked.CompareExchange(ref _current, null, scope);
    }
}
