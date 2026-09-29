using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Providers;

namespace NovelSpeaker.Application.Cache.Audio;

/// <summary>Transfers a synthesized stream into an owned temporary audio file.</summary>
public interface IGeneratedAudioFileStore
{
    Task<TtsAudioResponse> WriteAsync(ProviderSynthesisResult audio, CancellationToken cancellationToken);
}
