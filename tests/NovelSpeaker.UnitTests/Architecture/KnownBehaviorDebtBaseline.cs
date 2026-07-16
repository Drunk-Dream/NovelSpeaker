namespace NovelSpeaker.UnitTests.Architecture;

internal static class KnownBehaviorDebtBaseline
{
    public static readonly IReadOnlySet<string> AppOutputTestAudioFixtures =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "corrupt-tone.mp3",
            "demo-tone.mp3",
            "demo-tone.wav"
        };
}
