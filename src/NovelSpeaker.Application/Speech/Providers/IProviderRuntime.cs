using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public interface IProviderRuntime
{
    SpeechProviderType Type { get; }

    Task<ProviderSynthesisResult> SynthesizeAsync(
        SpeechProviderInstance provider,
        ProviderSynthesisRequest request,
        CancellationToken cancellationToken);
}
