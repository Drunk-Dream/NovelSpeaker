namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Enforces proactive per-rule request pacing for HTTP TTS execution.
/// </summary>
public interface ITtsRateLimiter
{
    Task WaitAsync(
        long ruleId,
        string? concurrentRate,
        CancellationToken cancellationToken);

    void ApplyRetryAfter(
        long ruleId,
        TimeSpan retryAfter);
}
