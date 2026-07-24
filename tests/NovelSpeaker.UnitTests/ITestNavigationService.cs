using NovelSpeaker.App.Navigation;
using Wpf.Ui;

namespace NovelSpeaker.UnitTests;

internal interface ITestNavigationService : INavigationService, IAppNavigator
{
    Task<bool> IAppNavigator.GoBackAsync(
        CancellationToken cancellationToken,
        bool bypassGuard) => Task.FromResult(GoBack());

    Task<bool> IAppNavigator.NavigateAsync(
        AppRoute route,
        CancellationToken cancellationToken,
        bool bypassGuard) => Task.FromResult(
            NavigateWithHierarchy(TestAppRouteMapper.GetPageType(route.Id), route));
}
