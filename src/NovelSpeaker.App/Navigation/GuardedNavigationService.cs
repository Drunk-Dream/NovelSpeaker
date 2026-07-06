using System.Threading;
using Wpf.Ui;

namespace NovelSpeaker.App.Navigation;

public sealed class GuardedNavigationService : IGuardedNavigationService
{
    private readonly INavigationGuardService _guardService;
    private readonly INavigationService _navigationService;
    private int _bypassDepth;

    public GuardedNavigationService(
        INavigationGuardService guardService,
        INavigationService navigationService)
    {
        _guardService = guardService;
        _navigationService = navigationService;
    }

    public bool IsBypassingGuard => Volatile.Read(ref _bypassDepth) > 0;

    public async Task<bool> GoBackAsync(CancellationToken cancellationToken, bool bypassGuard = false)
    {
        if (!bypassGuard && !await _guardService.ConfirmNavigationAsync(cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        using var _ = BeginBypass();
        return _navigationService.GoBack();
    }

    public async Task<bool> NavigateAsync(string pageIdOrTargetTag, CancellationToken cancellationToken, bool bypassGuard = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageIdOrTargetTag);

        if (!bypassGuard && !await _guardService.ConfirmNavigationAsync(cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        using var _ = BeginBypass();
        return _navigationService.Navigate(pageIdOrTargetTag);
    }

    public async Task<bool> NavigateWithHierarchyAsync(
        Type pageType,
        object? dataContext,
        CancellationToken cancellationToken,
        bool bypassGuard = false)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        if (!bypassGuard && !await _guardService.ConfirmNavigationAsync(cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        using var _ = BeginBypass();
        return _navigationService.NavigateWithHierarchy(pageType, dataContext);
    }

    private IDisposable BeginBypass()
    {
        Interlocked.Increment(ref _bypassDepth);
        return new Releaser(this);
    }

    private void EndBypass()
    {
        Interlocked.Decrement(ref _bypassDepth);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly GuardedNavigationService _owner;
        private bool _disposed;

        public Releaser(GuardedNavigationService owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.EndBypass();
        }
    }
}
