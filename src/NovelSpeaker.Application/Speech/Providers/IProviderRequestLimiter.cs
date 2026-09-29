using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public interface IProviderRequestLimiter
{
    Task<ITtsAdmissionLease> AcquireAsync(
        ProviderId providerId,
        ProviderRequestRateLimit? rateLimit,
        TtsAdmissionPriority priority,
        CancellationToken cancellationToken);

    void ApplyRetryAfter(ProviderId providerId, TimeSpan retryAfter);
}
