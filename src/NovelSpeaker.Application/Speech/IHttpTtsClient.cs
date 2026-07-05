using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Executes compiled HTTP TTS requests and validates returned audio.
/// </summary>
public interface IHttpTtsClient
{
    Task<TtsHttpExecutionResult> ExecuteAsync(
        ParsedTtsRequest request,
        CancellationToken cancellationToken);
}
