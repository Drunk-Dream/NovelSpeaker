using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui;

namespace NovelSpeaker.TestKit.Navigation;

internal interface ITestNavigationService : INavigationService, IAppNavigator
{
    AppRoute IAppNavigator.CurrentRoute => AppRoutes.Library;

    Task<bool> IAppNavigator.NavigateBackAsync(
        CancellationToken cancellationToken,
        bool bypassGuard) => Task.FromResult(false);

    Task<bool> IAppNavigator.NavigateAsync(
        AppRoute route,
        CancellationToken cancellationToken,
        bool bypassGuard) => Task.FromResult(
            NavigateWithHierarchy(TestAppRouteMapper.GetPageType(route.Id), route));
}
