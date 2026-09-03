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

    [Fact]
    public async Task Successful_navigation_keeps_the_complete_current_route()
    {
        var route = new BookDetailsRoute("book-42");
        var navigation = new RecordingNavigationService();
        var adapter = new ShellNavigationAdapter(new AllowNavigationGuard(), navigation);

        Assert.True(await adapter.NavigateAsync(route, CancellationToken.None));

        Assert.Same(route, adapter.CurrentRoute);
        Assert.Equal(AppRouteId.BookDetails, adapter.CurrentRouteId);
    }

    [Fact]
    public async Task Guard_rejection_and_framework_failure_preserve_the_current_route()
    {
        var initialRoute = AppRoutes.Library;
        var rejectedNavigation = new RecordingNavigationService();
        var rejectedAdapter = new ShellNavigationAdapter(
            new RejectNavigationGuard(),
            rejectedNavigation);

        Assert.False(await rejectedAdapter.NavigateAsync(
            new BookDetailsRoute("book-42"),
            CancellationToken.None));
        Assert.Same(initialRoute, rejectedAdapter.CurrentRoute);
        Assert.Equal(0, rejectedNavigation.NavigationCount);

        var failedNavigation = new RecordingNavigationService { NavigationResult = false };
        var failedAdapter = new ShellNavigationAdapter(new AllowNavigationGuard(), failedNavigation);

        Assert.False(await failedAdapter.NavigateAsync(
            new BookDetailsRoute("book-42"),
            CancellationToken.None));
        Assert.Same(initialRoute, failedAdapter.CurrentRoute);
    }

    [Fact]
    public async Task Cancellation_before_or_during_guard_stops_navigation_without_changing_the_route()
    {
        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        var preCancelledNavigation = new RecordingNavigationService();
        var preCancelledAdapter = new ShellNavigationAdapter(
            new AllowNavigationGuard(),
            preCancelledNavigation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preCancelledAdapter.NavigateAsync(
            AppRoutes.Settings,
            preCancelled.Token));
        Assert.Same(AppRoutes.Library, preCancelledAdapter.CurrentRoute);
        Assert.Equal(0, preCancelledNavigation.NavigationCount);

        using var duringGuardCancellation = new CancellationTokenSource();
        var guardEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGuard = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var duringGuardNavigation = new RecordingNavigationService();
        var duringGuardAdapter = new ShellNavigationAdapter(
            new BlockingNavigationGuard(guardEntered, releaseGuard.Task),
            duringGuardNavigation);
        var navigationTask = duringGuardAdapter.NavigateAsync(
            AppRoutes.Settings,
            duringGuardCancellation.Token);

        await guardEntered.Task;
        duringGuardCancellation.Cancel();
        releaseGuard.SetResult(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigationTask);
        Assert.Same(AppRoutes.Library, duringGuardAdapter.CurrentRoute);
        Assert.Equal(0, duringGuardNavigation.NavigationCount);
    }

    [Fact]
    public async Task Navigate_back_resolves_fixed_parent_routes_and_does_not_navigate_from_roots()
    {
        foreach (var (route, expectedParent) in new (AppRoute Route, AppRoute? Parent)[]
                 {
                     (new BookDetailsRoute("book-42"), AppRoutes.Library),
                     (AppRoutes.PlaybackSettings, AppRoutes.Settings),
                     (AppRoutes.TtsRules, AppRoutes.Settings),
                     (AppRoutes.ImportTextSettings, AppRoutes.Settings),
                     (AppRoutes.ChapterRules, AppRoutes.Settings),
                     (AppRoutes.CacheAndData, AppRoutes.Settings),
                     (AppRoutes.GeneralSettings, AppRoutes.Settings),
                     (AppRoutes.AppearanceSettings, AppRoutes.Settings),
                     (AppRoutes.DiagnosticsAbout, AppRoutes.Settings),
                     (AppRoutes.RegexReplacementRules, AppRoutes.ImportTextSettings),
                     (AppRoutes.CacheManagement, AppRoutes.CacheAndData),
                     (AppRoutes.Library, null),
                     (AppRoutes.Settings, null)
                 })
        {
            var navigation = new RecordingNavigationService();
            var adapter = new ShellNavigationAdapter(new AllowNavigationGuard(), navigation);

            Assert.True(await adapter.NavigateAsync(route, CancellationToken.None, bypassGuard: true));
            var navigatedBack = await adapter.NavigateBackAsync(
                CancellationToken.None,
                bypassGuard: true);

            Assert.Equal(expectedParent is not null, navigatedBack);
            if (expectedParent is null)
            {
                Assert.Equal(1, navigation.NavigationCount);
                Assert.Same(route, adapter.CurrentRoute);
            }
            else
            {
                Assert.Equal(2, navigation.NavigationCount);
                Assert.Same(expectedParent, adapter.CurrentRoute);
                Assert.Null(navigation.LastDataContext);
            }
        }
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
        public int NavigationCount { get; private set; }
        public bool NavigationResult { get; init; } = true;

        public INavigationView GetNavigationControl() => throw new NotSupportedException();
        public bool GoBack() => false;
        public bool Navigate(Type pageType) => Navigate(pageType, null);
        public bool Navigate(Type pageType, object? dataContext) => NavigateWithHierarchy(pageType, dataContext);
        public bool Navigate(string pageIdOrTargetTag) => false;
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => false;
        public bool NavigateWithHierarchy(Type pageType) => NavigateWithHierarchy(pageType, null);

        public bool NavigateWithHierarchy(Type pageType, object? dataContext)
        {
            NavigationCount++;
            LastPageType = pageType;
            LastDataContext = dataContext;
            return NavigationResult;
        }

        public void SetNavigationControl(INavigationView navigation)
        {
        }
    }

    private sealed class RejectNavigationGuard : INavigationGuardService
    {
        public IDisposable Register(Func<CancellationToken, Task<bool>> guard) => throw new NotSupportedException();

        public Task<bool> ConfirmNavigationAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class BlockingNavigationGuard : INavigationGuardService
    {
        private readonly TaskCompletionSource _entered;
        private readonly Task<bool> _release;

        public BlockingNavigationGuard(TaskCompletionSource entered, Task<bool> release)
        {
            _entered = entered;
            _release = release;
        }

        public IDisposable Register(Func<CancellationToken, Task<bool>> guard) => throw new NotSupportedException();

        public async Task<bool> ConfirmNavigationAsync(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            return await _release;
        }
    }
}
