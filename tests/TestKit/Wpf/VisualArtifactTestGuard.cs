namespace NovelSpeaker.TestKit.Wpf;

internal static class VisualArtifactTestGuard
{
    public static bool IsEnabled => string.Equals(
        Environment.GetEnvironmentVariable("NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS"),
        "1",
        StringComparison.Ordinal);
}
