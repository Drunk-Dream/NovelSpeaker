namespace NovelSpeaker.Infrastructure.IntegrationTests;

internal static class PlaybackTestAudio
{
    public static string DemoWavPath =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Audio", "demo-tone.wav");

    public static string DemoMp3Path =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Audio", "demo-tone.mp3");

    public static string CorruptMp3Path =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Audio", "corrupt-tone.mp3");
}
