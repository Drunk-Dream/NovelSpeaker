namespace NovelSpeaker.UnitTests.Architecture;

internal static class KnownArchitectureBaseline
{
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
        new HashSet<string>(StringComparer.Ordinal);
}
