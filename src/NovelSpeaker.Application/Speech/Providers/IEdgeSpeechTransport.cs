using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

/// <summary>Edge external I/O port; contains no HTTP, WebSocket, SSML or protocol DTO types.</summary>
public interface IEdgeSpeechTransport
{
    Task<IReadOnlyList<EdgeVoice>> GetVoicesAsync(CancellationToken cancellationToken);
    Task<ProviderSynthesisResult> SynthesizeAsync(EdgeVoice voice, string text, int ratePercent,
        CancellationToken cancellationToken);
}
