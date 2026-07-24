using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shell.Activation;

public sealed class ShellActivationCoordinator : IShellActivationCoordinator
{
    private readonly object _closeSyncRoot = new();
    private readonly INavigationGuardService _navigationGuardService;
    private readonly IShellLayoutController _shellLayoutController;
    private readonly IShellNavigationAdapter _navigationAdapter;
    private readonly IShellPlatformAdapter _platformAdapter;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CancellationToken _lifetimeToken;
    private Task? _closeTask;
    private bool _infrastructureConfigured;
    private bool _navigationInitialized;
    private bool _disposed;

    public ShellActivationCoordinator(
        INavigationGuardService navigationGuardService,
        IShellLayoutController shellLayoutController,
        IShellNavigationAdapter navigationAdapter,
        IShellPlatformAdapter platformAdapter)
    {
        _navigationGuardService = navigationGuardService;
        _shellLayoutController = shellLayoutController;
        _navigationAdapter = navigationAdapter;
        _platformAdapter = platformAdapter;
        _lifetimeToken = _lifetimeCancellation.Token;
    }

    public CancellationToken LifetimeToken => _lifetimeToken;

    public bool IsCloseApproved { get; private set; }

    public bool IsPlayerPageActive { get; private set; }

    public async Task ActivateAsync(ShellHostElements host, double windowWidth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(host);
        LifetimeToken.ThrowIfCancellationRequested();

        if (!_infrastructureConfigured)
        {
            _platformAdapter.ConfigureInfrastructure(host);
            _infrastructureConfigured = true;
        }

        _platformAdapter.ConfigureNavigationPresenter(host);
        if (_navigationInitialized)
        {
            _shellLayoutController.UpdateWindowWidth(windowWidth);
            return;
        }

        _platformAdapter.InitializeNavigation(host);
        _navigationInitialized = true;
        await _navigationAdapter.NavigateAsync(
            AppRoutes.Library,
            LifetimeToken,
            bypassGuard: true).ConfigureAwait(true);
        LifetimeToken.ThrowIfCancellationRequested();
        _shellLayoutController.UpdateWindowWidth(windowWidth);
    }

    public async Task HandleNavigationRequestAsync(
        NavigatingCancelEventArgs eventArgs,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(eventArgs);

        if (_navigationAdapter.IsBypassingGuard)
        {
            return;
        }

        eventArgs.Cancel = true;
        await _navigationAdapter.NavigateFromShellAsync(
            eventArgs,
            cancellationToken).ConfigureAwait(true);
    }

    public void HandleNavigated(EventArgs eventArgs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(eventArgs);

        _navigationAdapter.SynchronizeSelection(eventArgs);
        IsPlayerPageActive = _navigationAdapter.CurrentRouteId == AppRouteId.Player;
    }

    public Task RequestCloseAsync(Func<Task> closeWindowAsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(closeWindowAsync);

        lock (_closeSyncRoot)
        {
            if (_closeTask is not null)
            {
                return _closeTask;
            }

            var closeTask = ConfirmAndCloseAsync(closeWindowAsync);
            if (!closeTask.IsCompleted)
            {
                _closeTask = closeTask;
            }

            return closeTask;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    private async Task ConfirmAndCloseAsync(Func<Task> closeWindowAsync)
    {
        try
        {
            if (!await _navigationGuardService
                    .ConfirmNavigationAsync(LifetimeToken)
                    .ConfigureAwait(true))
            {
                return;
            }

            LifetimeToken.ThrowIfCancellationRequested();
            IsCloseApproved = true;
            await closeWindowAsync().ConfigureAwait(true);
        }
        catch
        {
            IsCloseApproved = false;
            throw;
        }
        finally
        {
            lock (_closeSyncRoot)
            {
                _closeTask = null;
            }
        }
    }
}
