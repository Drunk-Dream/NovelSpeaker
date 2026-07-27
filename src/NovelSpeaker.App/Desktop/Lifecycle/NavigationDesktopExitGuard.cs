using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Desktop.Lifecycle;

internal sealed class NavigationDesktopExitGuard : IDesktopExitGuard
{
    private readonly INavigationGuardService _navigationGuardService;

    public NavigationDesktopExitGuard(INavigationGuardService navigationGuardService)
    {
        _navigationGuardService = navigationGuardService;
    }

    public Task<bool> ConfirmExitAsync(CancellationToken cancellationToken)
    {
        return _navigationGuardService.ConfirmNavigationAsync(cancellationToken);
    }
}
