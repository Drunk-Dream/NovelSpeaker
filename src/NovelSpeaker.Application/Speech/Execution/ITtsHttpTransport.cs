using NovelSpeaker.Application.Speech.Compilation;

namespace NovelSpeaker.Application.Speech.Execution;

/// <summary>Owns one HTTP send operation and returns a transport-neutral response.</summary>
public interface ITtsHttpTransport
{
    Task<TtsTransportResult> SendAsync(ParsedTtsRequest request, CancellationToken cancellationToken);
}
