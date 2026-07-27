namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Queues shared per-rule TTS admission by priority, pacing, and one active execution lease.
/// </summary>
public interface ITtsRateLimiter
{
    /// <summary>
    /// Waits asynchronously for admission. Cancelling while queued consumes neither rate quota nor
    /// the execution permit; the caller must dispose the returned lease after the request finishes.
    /// </summary>
    Task<ITtsAdmissionLease> AcquireAsync(
        long ruleId,
        string? concurrentRate,
        TtsAdmissionPriority priority,
        CancellationToken cancellationToken);

    void ApplyRetryAfter(
        long ruleId,
        TimeSpan retryAfter);
}
