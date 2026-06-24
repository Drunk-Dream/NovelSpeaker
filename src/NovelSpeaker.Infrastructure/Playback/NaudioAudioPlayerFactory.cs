using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Creates standalone NAudio-backed players for flows that must not share coordinator state.
/// </summary>
public sealed class NaudioAudioPlayerFactory : IAudioPlayerFactory
{
    public IAudioPlayer Create()
    {
        return new NaudioAudioPlayer();
    }
}
