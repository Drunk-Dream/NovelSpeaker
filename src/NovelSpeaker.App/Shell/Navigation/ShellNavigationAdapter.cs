using System.Reflection;
using System.Threading;
using NovelSpeaker.App.Features.Appearance;
using NovelSpeaker.App.Features.BookDetails;
using NovelSpeaker.App.Features.Cache;
using NovelSpeaker.App.Features.ChapterRules;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Features.ImportTextSettings;
using NovelSpeaker.App.Features.GeneralSettings;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.App.Features.Playback;
using NovelSpeaker.App.Features.PlaybackSettings;
using NovelSpeaker.App.Features.RegexReplacementRules;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Features.TtsRules;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shell.Navigation;

public sealed class ShellNavigationAdapter : IShellNavigationAdapter
{
    private static readonly IReadOnlyDictionary<AppRouteId, Type> PageTypes =
        new Dictionary<AppRouteId, Type>
        {
            [AppRouteId.Library] = typeof(LibraryPage),
            [AppRouteId.BookDetails] = typeof(BookDetailsPage),
            [AppRouteId.Player] = typeof(PlayerPage),
            [AppRouteId.Settings] = typeof(SettingsPage),
            [AppRouteId.PlaybackSettings] = typeof(PlaybackSettingsPage),
            [AppRouteId.TtsRules] = typeof(TtsRulesPage),
            [AppRouteId.ImportTextSettings] = typeof(ImportTextSettingsPage),
            [AppRouteId.RegexReplacementRules] = typeof(RegexReplacementRulesPage),
            [AppRouteId.ChapterRules] = typeof(ChapterRulesPage),
            [AppRouteId.CacheAndData] = typeof(CacheAndDataPage),
            [AppRouteId.CacheManagement] = typeof(CacheManagementPage),
            [AppRouteId.GeneralSettings] = typeof(GeneralSettingsPage),
            [AppRouteId.AppearanceSettings] = typeof(AppearanceSettingsPage),
            [AppRouteId.DiagnosticsAbout] = typeof(DiagnosticsAboutPage)
        };

    private readonly INavigationGuardService _guardService;
    private readonly INavigationService _navigationService;
    private int _bypassDepth;
    private INavigationView? _navigationView;
    private NavigationViewItem? _libraryItem;
    private NavigationViewItem? _settingsItem;
    private NavigationViewItem? _playbackItem;
    private NavigationViewItem? _currentPrimaryItem;

    public AppRoute CurrentRoute { get; private set; } = AppRoutes.Library;

    public AppRouteId CurrentRouteId => CurrentRoute.Id;

    public ShellNavigationAdapter(
        INavigationGuardService guardService,
        INavigationService navigationService)
    {
        _guardService = guardService;
        _navigationService = navigationService;
    }

    public bool IsBypassingGuard => Volatile.Read(ref _bypassDepth) > 0;

    public bool IsPlayerPageActive { get; private set; }

    public void Initialize(
        INavigationView navigationView,
        NavigationViewItem libraryItem,
        NavigationViewItem settingsItem,
        NavigationViewItem playbackItem)
    {
        ArgumentNullException.ThrowIfNull(navigationView);
        ArgumentNullException.ThrowIfNull(libraryItem);
        ArgumentNullException.ThrowIfNull(settingsItem);
        ArgumentNullException.ThrowIfNull(playbackItem);

        _navigationView = navigationView;
        _libraryItem = libraryItem;
        _settingsItem = settingsItem;
        _playbackItem = playbackItem;

        libraryItem.TargetPageType = PageTypes[AppRouteId.Library];
        settingsItem.TargetPageType = PageTypes[AppRouteId.Settings];
        _navigationService.SetNavigationControl(navigationView);
        ApplySelection(AppRouteId.Library);
    }

    public async Task<bool> NavigateBackAsync(
        CancellationToken cancellationToken,
        bool bypassGuard = false)
    {
        var parentRoute = ResolveParentRoute(CurrentRoute);
        if (parentRoute is null)
        {
            return false;
        }

        return await NavigateAsync(parentRoute, cancellationToken, bypassGuard).ConfigureAwait(true);
    }

    public async Task<bool> NavigateAsync(
        AppRoute route,
        CancellationToken cancellationToken,
        bool bypassGuard = false)
    {
        ArgumentNullException.ThrowIfNull(route);
        ValidateRoute(route);
        cancellationToken.ThrowIfCancellationRequested();

        if (!bypassGuard &&
            !await _guardService.ConfirmNavigationAsync(cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var _ = BeginBypass();
        var navigated = _navigationService.NavigateWithHierarchy(
            PageTypes[route.Id],
            ToNavigationData(route));
        if (navigated)
        {
            CurrentRoute = route;
            IsPlayerPageActive = route.Id == AppRouteId.Player;
            ApplySelection(route.Id);
        }
        else
        {
            IsPlayerPageActive = CurrentRoute.Id == AppRouteId.Player;
        }

        return navigated;
    }

    public async Task<bool> NavigateFromShellAsync(
        NavigatingCancelEventArgs eventArgs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        var pageId = ReadProperty(eventArgs, "PageId") as string;
        var page = ReadProperty(eventArgs, "Page");
        var pageType = page as Type ?? page?.GetType();
        if (!TryResolveRoute(pageType, pageId, out var route))
        {
            return false;
        }

        var navigated = await NavigateAsync(route, cancellationToken).ConfigureAwait(true);
        if (!navigated)
        {
            ReapplySelection();
        }

        return navigated;
    }

    public void SynchronizeSelection(EventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        var page = ReadProperty(eventArgs, "Page");
        var pageType = page as Type ?? page?.GetType();
        if (TryResolveRouteId(pageType, null, out var routeId))
        {
            IsPlayerPageActive = routeId == AppRouteId.Player;
            ApplySelection(routeId);
        }
    }

    private static object? ToNavigationData(AppRoute route)
    {
        return route switch
        {
            BookDetailsRoute bookDetails => bookDetails,
            PlayerRoute player => player,
            _ => null
        };
    }

    private static void ValidateRoute(AppRoute route)
    {
        var valid = route switch
        {
            BookDetailsRoute => route.Id == AppRouteId.BookDetails,
            PlayerRoute => route.Id == AppRouteId.Player,
            ParameterlessAppRoute => route.Id is not AppRouteId.BookDetails and not AppRouteId.Player,
            _ => false
        };

        if (!valid || !PageTypes.ContainsKey(route.Id))
        {
            throw new ArgumentException("The route and its parameters do not match a registered App route.", nameof(route));
        }
    }

    private static AppRoute? ResolveParentRoute(AppRoute route)
    {
        return route switch
        {
            BookDetailsRoute => AppRoutes.Library,
            ParameterlessAppRoute parameterless => parameterless.Id switch
            {
                AppRouteId.PlaybackSettings or
                AppRouteId.TtsRules or
                AppRouteId.ImportTextSettings or
                AppRouteId.ChapterRules or
                AppRouteId.CacheAndData or
                AppRouteId.GeneralSettings or
                AppRouteId.AppearanceSettings or
                AppRouteId.DiagnosticsAbout => AppRoutes.Settings,
                AppRouteId.RegexReplacementRules => AppRoutes.ImportTextSettings,
                AppRouteId.CacheManagement => AppRoutes.CacheAndData,
                AppRouteId.Library or AppRouteId.Settings => null,
                _ => throw new InvalidOperationException("The current route has no registered parent route.")
            },
            PlayerRoute player => player.ReturnRoute,
            _ => throw new InvalidOperationException("The current route has no registered parent route.")
        };
    }

    private static bool TryResolveRoute(Type? pageType, string? pageId, out AppRoute route)
    {
        if (TryResolveRouteId(pageType, pageId, out var routeId))
        {
            route = CreateShellRoute(routeId);
            return true;
        }

        route = null!;
        return false;
    }

    private static bool TryResolveRouteId(Type? pageType, string? pageId, out AppRouteId routeId)
    {
        var pageMatch = PageTypes.FirstOrDefault(pair => pair.Value == pageType);
        if (pageType is not null && pageMatch.Value is not null)
        {
            routeId = pageMatch.Key;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(pageId))
        {
            var idMatch = PageTypes.FirstOrDefault(
                pair => string.Equals(pair.Value.FullName, pageId, StringComparison.Ordinal) ||
                        string.Equals(pair.Value.Name, pageId, StringComparison.Ordinal));
            if (idMatch.Value is not null)
            {
                routeId = idMatch.Key;
                return true;
            }
        }

        routeId = default;
        return false;
    }

    private static AppRoute CreateShellRoute(AppRouteId routeId)
    {
        return routeId switch
        {
            AppRouteId.BookDetails or AppRouteId.Player =>
                throw new InvalidOperationException("Parameterized routes cannot be created by a shell menu item."),
            _ => new ParameterlessAppRoute(routeId)
        };
    }

    private void ApplySelection(AppRouteId routeId)
    {
        if (_navigationView is not NavigationView navigationView ||
            _libraryItem is null ||
            _settingsItem is null ||
            _playbackItem is null)
        {
            return;
        }

        var primaryItem = IsSettingsContext(routeId) ? _settingsItem : _libraryItem;
        _currentPrimaryItem = primaryItem;

        if (!ReferenceEquals(navigationView.SelectedItem, primaryItem))
        {
            // Wpf.Ui 4.x exposes SelectedItem with a non-public setter.
            typeof(NavigationView)
                .GetProperty(nameof(NavigationView.SelectedItem), BindingFlags.Instance | BindingFlags.Public)?
                .GetSetMethod(nonPublic: true)?
                .Invoke(navigationView, [primaryItem]);
        }

        _libraryItem.IsActive = ReferenceEquals(primaryItem, _libraryItem);
        _settingsItem.IsActive = ReferenceEquals(primaryItem, _settingsItem);
        _playbackItem.IsActive = false;
    }

    private void ReapplySelection()
    {
        if (_currentPrimaryItem is null || _libraryItem is null)
        {
            return;
        }

        ApplySelection(CurrentRoute.Id);
    }

    private static bool IsSettingsContext(AppRouteId routeId)
    {
        return routeId is AppRouteId.Settings
            or AppRouteId.PlaybackSettings
            or AppRouteId.TtsRules
            or AppRouteId.ImportTextSettings
            or AppRouteId.RegexReplacementRules
            or AppRouteId.ChapterRules
            or AppRouteId.CacheAndData
            or AppRouteId.CacheManagement
            or AppRouteId.GeneralSettings
            or AppRouteId.AppearanceSettings
            or AppRouteId.DiagnosticsAbout;
    }

    private IDisposable BeginBypass()
    {
        Interlocked.Increment(ref _bypassDepth);
        return new Releaser(this);
    }

    private static object? ReadProperty(object source, string propertyName)
    {
        return source.GetType().GetProperty(propertyName)?.GetValue(source);
    }

    private sealed class Releaser(ShellNavigationAdapter owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Interlocked.Decrement(ref owner._bypassDepth);
        }
    }
}
