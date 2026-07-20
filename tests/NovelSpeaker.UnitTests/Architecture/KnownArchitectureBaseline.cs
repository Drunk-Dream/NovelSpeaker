namespace NovelSpeaker.UnitTests.Architecture;

internal static class KnownArchitectureBaseline
{
    public static readonly IReadOnlySet<string> ApplicationForbiddenSourceDependencies =
        new HashSet<string>(StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> AppInfrastructureSourceFiles =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "src/NovelSpeaker.App/App.xaml.cs"
        };

    public static readonly IReadOnlySet<string> SourceLayoutViolations =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "src/NovelSpeaker.App/Feedback/AppDialogDecision.cs: public types [AppConfirmationDecision, UnsavedChangesDecision], expected primary type 'AppDialogDecision'",
        };

    public static readonly IReadOnlySet<string> ViewModelForbiddenPublicApiDependencies =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "NovelSpeaker.App.ViewModels.AppearanceSettingsViewModel..ctor(navigationService) -> Wpf.Ui.INavigationService",
            "NovelSpeaker.App.ViewModels.CacheAndDataViewModel..ctor(navigationService) -> Wpf.Ui.INavigationService",
            "NovelSpeaker.App.ViewModels.CacheManagementViewModel..ctor(navigationService) -> Wpf.Ui.INavigationService",
            "NovelSpeaker.App.ViewModels.ChapterRulesViewModel..ctor(navigationService) -> Wpf.Ui.INavigationService",
            "NovelSpeaker.App.ViewModels.DiagnosticsAboutViewModel..ctor(navigationService) -> Wpf.Ui.INavigationService",
            "NovelSpeaker.App.ViewModels.ImportTextSettingsViewModel..ctor(navigationService) -> Wpf.Ui.INavigationService",
            "NovelSpeaker.App.ViewModels.MainWindowViewModel.NowPlayingSymbol -> Wpf.Ui.Controls.SymbolRegular",
            "NovelSpeaker.App.ViewModels.PlaybackSettingsViewModel..ctor(navigationService) -> Wpf.Ui.INavigationService",
            "NovelSpeaker.App.ViewModels.PlayerSegmentItemViewModel.FontWeight -> System.Windows.FontWeight",
            "NovelSpeaker.App.ViewModels.PlayerViewModel..ctor(navigationService) -> Wpf.Ui.INavigationService",
            "NovelSpeaker.App.ViewModels.PlayerViewModel.PrimaryActionSymbol -> Wpf.Ui.Controls.SymbolRegular",
            "NovelSpeaker.App.ViewModels.RegexReplacementRulesViewModel..ctor(navigation) -> Wpf.Ui.INavigationService",
            "NovelSpeaker.App.ViewModels.SettingsNavigationItemViewModel..ctor(IconSymbol) -> Wpf.Ui.Controls.SymbolRegular",
            "NovelSpeaker.App.ViewModels.SettingsNavigationItemViewModel.Deconstruct(IconSymbol) -> Wpf.Ui.Controls.SymbolRegular",
            "NovelSpeaker.App.ViewModels.SettingsNavigationItemViewModel.IconSymbol -> Wpf.Ui.Controls.SymbolRegular",
            "NovelSpeaker.App.ViewModels.SettingsViewModel..ctor(navigationService) -> Wpf.Ui.INavigationService",
            "NovelSpeaker.App.ViewModels.TtsRulesViewModel..ctor(navigationService) -> Wpf.Ui.INavigationService"
        };
}
