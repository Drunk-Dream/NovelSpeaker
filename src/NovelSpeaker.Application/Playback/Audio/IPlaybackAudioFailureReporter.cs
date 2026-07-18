namespace NovelSpeaker.Application.Playback.Audio;

/// <summary>Reports playback-audio orchestration failures through a safe technical diagnostics boundary.</summary>
public interface IPlaybackAudioFailureReporter
{
    void Report(string operation, Exception exception, PlaybackAudioRequest request);
}
