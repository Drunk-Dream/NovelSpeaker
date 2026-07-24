namespace NovelSpeaker.App.Shell.Navigation;

public interface IAppNavigator
{
    Task<bool> NavigateAsync(
        AppRoute route,
        CancellationToken cancellationToken,
        bool bypassGuard = false);

    Task<bool> GoBackAsync(
        CancellationToken cancellationToken,
        bool bypassGuard = false);
}
