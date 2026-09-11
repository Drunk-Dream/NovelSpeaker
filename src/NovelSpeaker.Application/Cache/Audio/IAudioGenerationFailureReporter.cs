namespace NovelSpeaker.Application.Cache.Audio;

/// <summary>Reports playback-audio orchestration failures through a safe technical diagnostics boundary.</summary>
public interface IAudioGenerationFailureReporter
{
    void Report(string operation, Exception exception, AudioGenerationRequest request);
}
