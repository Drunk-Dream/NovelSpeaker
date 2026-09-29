using System.Collections.Concurrent;
using System.Globalization;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>Adapts typed Provider limits to the shared priority admission queue.</summary>
public sealed class ProviderRequestLimiter(ITtsRateLimiter limiter) : IProviderRequestLimiter
{
    private readonly ConcurrentDictionary<ProviderId, long> _ids = new();
    private long _nextId;

    public Task<ITtsAdmissionLease> AcquireAsync(
        ProviderId providerId,
        ProviderRequestRateLimit? rateLimit,
        TtsAdmissionPriority priority,
        CancellationToken cancellationToken) =>
        limiter.AcquireAsync(GetId(providerId), rateLimit is null
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{rateLimit.MaxRequests}/{rateLimit.WindowMilliseconds}"),
            priority, cancellationToken);

    public void ApplyRetryAfter(ProviderId providerId, TimeSpan retryAfter) =>
        limiter.ApplyRetryAfter(GetId(providerId), retryAfter);

    private long GetId(ProviderId providerId) =>
        _ids.GetOrAdd(providerId, _ => Interlocked.Decrement(ref _nextId));
}
