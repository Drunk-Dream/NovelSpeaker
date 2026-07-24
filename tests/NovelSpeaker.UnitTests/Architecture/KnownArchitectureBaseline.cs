namespace NovelSpeaker.UnitTests.Architecture;

internal static class KnownArchitectureBaseline
{
    public static readonly IReadOnlySet<string> ApplicationForbiddenSourceDependencies =
        new HashSet<string>(StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> AppInfrastructureSourceFiles =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "src/NovelSpeaker.App/Bootstrap/App.xaml.cs"
        };

    public static readonly IReadOnlySet<string> SourceLayoutViolations =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "src/NovelSpeaker.App/Shared/Dialogs/AppDialogDecision.cs: public types [AppConfirmationDecision, UnsavedChangesDecision], expected primary type 'AppDialogDecision'",
        };

    public static readonly IReadOnlySet<string> ViewModelForbiddenPublicApiDependencies =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "NovelSpeaker.App.Features.Playback.Presentation.PlayerSegmentItemViewModel.FontWeight -> System.Windows.FontWeight",
            "NovelSpeaker.App.Features.Playback.Presentation.PlayerViewModel.PrimaryActionSymbol -> Wpf.Ui.Controls.SymbolRegular",
            "NovelSpeaker.App.Features.Settings.SettingsNavigationItemViewModel..ctor(IconSymbol) -> Wpf.Ui.Controls.SymbolRegular",
            "NovelSpeaker.App.Features.Settings.SettingsNavigationItemViewModel.Deconstruct(IconSymbol) -> Wpf.Ui.Controls.SymbolRegular",
            "NovelSpeaker.App.Features.Settings.SettingsNavigationItemViewModel.IconSymbol -> Wpf.Ui.Controls.SymbolRegular"
        };
}
