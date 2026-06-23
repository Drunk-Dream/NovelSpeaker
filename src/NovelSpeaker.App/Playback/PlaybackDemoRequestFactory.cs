using System.IO;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.App.Playback;

/// <summary>
/// Maps bundled demo audio assets into playback requests.
/// </summary>
public sealed class PlaybackDemoRequestFactory : IPlaybackDemoRequestFactory
{
    private const string DemoBookId = "demo-book";

    public PlaybackRequest CreateWavDemoRequest()
    {
        return CreateRequest("demo-tone.wav", "内置演示 WAV", 0);
    }

    public PlaybackRequest CreateMp3DemoRequest()
    {
        return CreateRequest("demo-tone.mp3", "内置演示 MP3", 1);
    }

    public PlaybackRequest CreateCorruptDemoRequest()
    {
        return CreateRequest("corrupt-tone.mp3", "损坏演示音频", 2);
    }

    private static PlaybackRequest CreateRequest(string fileName, string displayTitle, int segmentIndex)
    {
        return new PlaybackRequest(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", fileName),
            displayTitle,
            DemoBookId,
            0,
            segmentIndex,
            0,
            false);
    }
}
