namespace NovelSpeaker.UnitTests.Architecture;

internal static class KnownBehaviorDebtBaseline
{
    public static readonly IReadOnlySet<string> RulePagesWithoutGlobalNavigationGuard =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "src/NovelSpeaker.App/Pages/ChapterRulesPage.xaml.cs",
            "src/NovelSpeaker.App/Pages/RegexReplacementRulesPage.xaml.cs",
            "src/NovelSpeaker.App/Pages/TtsRulesPage.xaml.cs"
        };

    public static readonly IReadOnlySet<string> AppOutputTestAudioFixtures =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "corrupt-tone.mp3",
            "demo-tone.mp3",
            "demo-tone.wav"
        };
}
