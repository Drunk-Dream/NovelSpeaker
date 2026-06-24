using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Executes compiled HTTP TTS requests, manages per-rule cookies, and validates returned audio.
/// </summary>
public interface IHttpTtsClient
{
    Task<TtsHttpExecutionResult> ExecuteAsync(
        ParsedTtsRequest request,
        CancellationToken cancellationToken);

    Task ClearRuleCookiesAsync(
        long ruleId,
        CancellationToken cancellationToken);
}
