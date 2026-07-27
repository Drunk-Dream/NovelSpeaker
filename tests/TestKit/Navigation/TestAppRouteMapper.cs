using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.TestKit.Navigation;

internal static class TestAppRouteMapper
{
    public static Type GetPageType(AppRouteId routeId) => routeId switch
    {
        AppRouteId.Library => typeof(LibraryPage),
        AppRouteId.BookDetails => typeof(BookDetailsPage),
        AppRouteId.Player => typeof(PlayerPage),
        AppRouteId.Settings => typeof(SettingsPage),
        AppRouteId.PlaybackSettings => typeof(PlaybackSettingsPage),
        AppRouteId.TtsRules => typeof(TtsRulesPage),
        AppRouteId.ImportTextSettings => typeof(ImportTextSettingsPage),
        AppRouteId.RegexReplacementRules => typeof(RegexReplacementRulesPage),
        AppRouteId.ChapterRules => typeof(ChapterRulesPage),
        AppRouteId.CacheAndData => typeof(CacheAndDataPage),
        AppRouteId.CacheManagement => typeof(CacheManagementPage),
        AppRouteId.GeneralSettings => typeof(GeneralSettingsPage),
        AppRouteId.AppearanceSettings => typeof(AppearanceSettingsPage),
        AppRouteId.DiagnosticsAbout => typeof(DiagnosticsAboutPage),
        _ => throw new ArgumentOutOfRangeException(nameof(routeId))
    };
}
