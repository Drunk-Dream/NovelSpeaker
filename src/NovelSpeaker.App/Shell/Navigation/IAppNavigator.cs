namespace NovelSpeaker.App.Shell.Navigation;

public interface IAppNavigator
{
    AppRoute CurrentRoute { get; }

    Task<bool> NavigateAsync(
        AppRoute route,
        CancellationToken cancellationToken,
        bool bypassGuard = false);

    Task<bool> NavigateBackAsync(
        CancellationToken cancellationToken,
        bool bypassGuard = false);
}
