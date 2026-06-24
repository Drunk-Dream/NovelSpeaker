namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Creates independent local audio player instances for isolated playback flows.
/// </summary>
public interface IAudioPlayerFactory
{
    IAudioPlayer Create();
}
