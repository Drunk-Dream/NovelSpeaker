using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.App.Playback;

/// <summary>
/// Creates local demo playback requests for the desktop player page.
/// </summary>
public interface IPlaybackDemoRequestFactory
{
    PlaybackRequest CreateWavDemoRequest();
    PlaybackRequest CreateMp3DemoRequest();
    PlaybackRequest CreateCorruptDemoRequest();
}
