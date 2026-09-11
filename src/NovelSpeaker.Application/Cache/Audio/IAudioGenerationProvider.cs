namespace NovelSpeaker.Application.Cache.Audio;

/// <summary>
/// Produces a local audio file for one playback segment, optionally via cache.
/// </summary>
public interface IAudioGenerationProvider
{
    Task<AudioGenerationResult> GetAudioAsync(
        AudioGenerationRequest request,
        AudioGenerationPriority priority,
        Action<AudioGenerationProgress>? progressCallback,
        CancellationToken cancellationToken);

    Task InvalidateAsync(AudioGenerationRequest request, CancellationToken cancellationToken);
}
