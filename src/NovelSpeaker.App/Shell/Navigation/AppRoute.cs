namespace NovelSpeaker.App.Shell.Navigation;

public enum AppRouteId
{
    Library,
    BookDetails,
    Player,
    Settings,
    PlaybackSettings,
    TtsRules,
    ImportTextSettings,
    RegexReplacementRules,
    ChapterRules,
    CacheAndData,
    CacheManagement,
    GeneralSettings,
    AppearanceSettings,
    DiagnosticsAbout
}

public enum PlayerNavigationMode
{
    OpenPaused = 0,
    ReturnToCurrentSession = 1
}

public abstract record AppRoute(AppRouteId Id);

public sealed record ParameterlessAppRoute : AppRoute
{
    public ParameterlessAppRoute(AppRouteId id)
        : base(id)
    {
        if (id is AppRouteId.BookDetails or AppRouteId.Player)
        {
            throw new ArgumentException("The route requires strongly typed parameters.", nameof(id));
        }
    }
}

public sealed record BookDetailsRoute(string BookId) : AppRoute(AppRouteId.BookDetails)
{
    public string BookId { get; init; } = !string.IsNullOrWhiteSpace(BookId)
        ? BookId
        : throw new ArgumentException("A book id is required.", nameof(BookId));
}

public sealed record PlayerRoute(
    string BookId,
    PlayerNavigationMode Mode = PlayerNavigationMode.OpenPaused,
    int? ChapterIndex = null,
    int? SegmentIndex = null) : AppRoute(AppRouteId.Player)
{
    public string BookId { get; init; } = !string.IsNullOrWhiteSpace(BookId)
        ? BookId
        : throw new ArgumentException("A book id is required.", nameof(BookId));
}

public static class AppRoutes
{
    public static AppRoute Library { get; } = new ParameterlessAppRoute(AppRouteId.Library);
    public static AppRoute Settings { get; } = new ParameterlessAppRoute(AppRouteId.Settings);
    public static AppRoute PlaybackSettings { get; } = new ParameterlessAppRoute(AppRouteId.PlaybackSettings);
    public static AppRoute TtsRules { get; } = new ParameterlessAppRoute(AppRouteId.TtsRules);
    public static AppRoute ImportTextSettings { get; } = new ParameterlessAppRoute(AppRouteId.ImportTextSettings);
    public static AppRoute RegexReplacementRules { get; } = new ParameterlessAppRoute(AppRouteId.RegexReplacementRules);
    public static AppRoute ChapterRules { get; } = new ParameterlessAppRoute(AppRouteId.ChapterRules);
    public static AppRoute CacheAndData { get; } = new ParameterlessAppRoute(AppRouteId.CacheAndData);
    public static AppRoute CacheManagement { get; } = new ParameterlessAppRoute(AppRouteId.CacheManagement);
    public static AppRoute GeneralSettings { get; } = new ParameterlessAppRoute(AppRouteId.GeneralSettings);
    public static AppRoute AppearanceSettings { get; } = new ParameterlessAppRoute(AppRouteId.AppearanceSettings);
    public static AppRoute DiagnosticsAbout { get; } = new ParameterlessAppRoute(AppRouteId.DiagnosticsAbout);
}
