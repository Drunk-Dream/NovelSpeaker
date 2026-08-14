using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.PresentationTests;

public sealed class AppRouteNavigationTests
{
    [Fact]
    public async Task Every_app_route_maps_to_one_wpf_page_in_shell_adapter()
    {
        foreach (var (route, expectedPageType) in new (AppRoute Route, Type PageType)[]
                 {
                     (AppRoutes.Library, typeof(LibraryPage)),
                     (new BookDetailsRoute("book-1"), typeof(BookDetailsPage)),
                     (new PlayerRoute("book-1"), typeof(PlayerPage)),
                     (AppRoutes.Settings, typeof(SettingsPage)),
                     (AppRoutes.PlaybackSettings, typeof(PlaybackSettingsPage)),
                     (AppRoutes.TtsRules, typeof(TtsRulesPage)),
                     (AppRoutes.ImportTextSettings, typeof(ImportTextSettingsPage)),
                     (AppRoutes.RegexReplacementRules, typeof(RegexReplacementRulesPage)),
                     (AppRoutes.ChapterRules, typeof(ChapterRulesPage)),
                     (AppRoutes.CacheAndData, typeof(CacheAndDataPage)),
                     (AppRoutes.CacheManagement, typeof(CacheManagementPage)),
                     (AppRoutes.GeneralSettings, typeof(GeneralSettingsPage)),
                     (AppRoutes.AppearanceSettings, typeof(AppearanceSettingsPage)),
                     (AppRoutes.DiagnosticsAbout, typeof(DiagnosticsAboutPage))
                 })
        {
            var navigation = new RecordingNavigationService();
            var adapter = new ShellNavigationAdapter(new AllowNavigationGuard(), navigation);

            Assert.True(await adapter.NavigateAsync(route, CancellationToken.None));
            Assert.Equal(expectedPageType, navigation.LastPageType);
            if (route is BookDetailsRoute or PlayerRoute)
            {
                Assert.Same(route, navigation.LastDataContext);
            }
            else
            {
                Assert.Null(navigation.LastDataContext);
            }
        }
    }

    [Fact]
    public void Parameterless_route_rejects_parameterized_route_ids()
    {
        Assert.Throws<ArgumentException>(() => new ParameterlessAppRoute(AppRouteId.BookDetails));
        Assert.Throws<ArgumentException>(() => new ParameterlessAppRoute(AppRouteId.Player));
    }

    private sealed class AllowNavigationGuard : INavigationGuardService
    {
        public IDisposable Register(Func<CancellationToken, Task<bool>> guard) => throw new NotSupportedException();

        public Task<bool> ConfirmNavigationAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class RecordingNavigationService : INavigationService
    {
        public Type? LastPageType { get; private set; }
        public object? LastDataContext { get; private set; }

        public INavigationView GetNavigationControl() => throw new NotSupportedException();
        public bool GoBack() => false;
        public bool Navigate(Type pageType) => Navigate(pageType, null);
        public bool Navigate(Type pageType, object? dataContext) => NavigateWithHierarchy(pageType, dataContext);
        public bool Navigate(string pageIdOrTargetTag) => false;
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => false;
        public bool NavigateWithHierarchy(Type pageType) => NavigateWithHierarchy(pageType, null);

        public bool NavigateWithHierarchy(Type pageType, object? dataContext)
        {
            LastPageType = pageType;
            LastDataContext = dataContext;
            return true;
        }

        public void SetNavigationControl(INavigationView navigation)
        {
        }
    }
}
