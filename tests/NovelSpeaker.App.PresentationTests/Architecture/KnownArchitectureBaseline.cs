namespace NovelSpeaker.App.PresentationTests.Architecture;

internal static class KnownArchitectureBaseline
{
    public static readonly IReadOnlySet<string> AppInfrastructureSourceFiles =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "src/NovelSpeaker.App/Bootstrap/WpfStartupRuntime.cs"
        };

    public static readonly IReadOnlySet<string> SourceLayoutViolations =
        new HashSet<string>(StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> ViewModelForbiddenPublicApiDependencies =
        new HashSet<string>(StringComparer.Ordinal);
}
