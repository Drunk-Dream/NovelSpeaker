using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;

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
