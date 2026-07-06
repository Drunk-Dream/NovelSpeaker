namespace NovelSpeaker.App.Navigation;

public interface INavigationGuardService
{
    IDisposable Register(Func<CancellationToken, Task<bool>> guard);

    Task<bool> ConfirmNavigationAsync(CancellationToken cancellationToken);
}
