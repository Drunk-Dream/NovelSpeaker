namespace NovelSpeaker.App.Navigation;

public interface IGuardedNavigationService
{
    bool IsBypassingGuard { get; }

    Task<bool> GoBackAsync(CancellationToken cancellationToken, bool bypassGuard = false);

    Task<bool> NavigateAsync(string pageIdOrTargetTag, CancellationToken cancellationToken, bool bypassGuard = false);

    Task<bool> NavigateWithHierarchyAsync(
        Type pageType,
        object? dataContext,
        CancellationToken cancellationToken,
        bool bypassGuard = false);
}
