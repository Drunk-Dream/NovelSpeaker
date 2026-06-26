using System.Collections.Concurrent;
using NovelSpeaker.Application.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>
/// Enforces proactive request pacing and shared server backoff per TTS rule.
/// </summary>
public sealed class TtsRateLimiter : ITtsRateLimiter
{
    private readonly ConcurrentDictionary<long, RuleState> _states = new();
    private readonly TimeProvider _timeProvider;

    public TtsRateLimiter(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task WaitAsync(
        long ruleId,
        string? concurrentRate,
        CancellationToken cancellationToken)
    {
        var policy = RateLimitPolicy.Parse(concurrentRate);
        var state = _states.GetOrAdd(ruleId, static _ => new RuleState());
        await state.Mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        var holdsMutex = true;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var now = _timeProvider.GetUtcNow();
                if (policy is not null)
                {
                    TrimWindow(state, policy, now);
                }

                var waitTime = GetRequiredWait(state, policy, now);
                if (waitTime <= TimeSpan.Zero)
                {
                    if (policy is not null)
                    {
                        state.RequestTimestamps.Enqueue(now);
                        if (state.RequestTimestamps.Count > policy.MaxRequests)
                        {
                            state.RequestTimestamps.Dequeue();
                        }
                    }

                    if (state.BlockedUntilUtc <= now)
                    {
                        state.BlockedUntilUtc = DateTimeOffset.MinValue;
                    }

                    return;
                }

                state.Mutex.Release();
                holdsMutex = false;
                try
                {
                    await Task.Delay(waitTime, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await state.Mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
                    holdsMutex = true;
                }
            }
        }
        finally
        {
            if (holdsMutex)
            {
                state.Mutex.Release();
            }
        }
    }

    public void ApplyRetryAfter(long ruleId, TimeSpan retryAfter)
    {
        if (retryAfter < TimeSpan.Zero)
        {
            retryAfter = TimeSpan.Zero;
        }

        var state = _states.GetOrAdd(ruleId, static _ => new RuleState());
        state.Mutex.Wait();
        try
        {
            var blockedUntil = _timeProvider.GetUtcNow() + retryAfter;
            if (blockedUntil > state.BlockedUntilUtc)
            {
                state.BlockedUntilUtc = blockedUntil;
            }
        }
        finally
        {
            state.Mutex.Release();
        }
    }

    private static void TrimWindow(RuleState state, RateLimitPolicy policy, DateTimeOffset now)
    {
        while (state.RequestTimestamps.Count > 0 &&
               now - state.RequestTimestamps.Peek() >= policy.Window)
        {
            state.RequestTimestamps.Dequeue();
        }
    }

    private static TimeSpan GetRequiredWait(RuleState state, RateLimitPolicy? policy, DateTimeOffset now)
    {
        var waitTime = state.BlockedUntilUtc > now
            ? state.BlockedUntilUtc - now
            : TimeSpan.Zero;

        if (policy is null)
        {
            return waitTime;
        }

        if (state.RequestTimestamps.Count < policy.MaxRequests)
        {
            return waitTime;
        }

        var oldest = state.RequestTimestamps.Peek();
        var windowWait = (oldest + policy.Window) - now;
        return windowWait > waitTime ? windowWait : waitTime;
    }

    private sealed class RuleState
    {
        public SemaphoreSlim Mutex { get; } = new(1, 1);

        public Queue<DateTimeOffset> RequestTimestamps { get; } = new();

        public DateTimeOffset BlockedUntilUtc { get; set; } = DateTimeOffset.MinValue;
    }

    private sealed record RateLimitPolicy(int MaxRequests, TimeSpan Window)
    {
        public static RateLimitPolicy? Parse(string? concurrentRate)
        {
            if (string.IsNullOrWhiteSpace(concurrentRate))
            {
                return null;
            }

            var value = concurrentRate.Trim();
            if (long.TryParse(value, out var minimumIntervalMs))
            {
                if (minimumIntervalMs <= 0)
                {
                    throw new FormatException("concurrentRate 的毫秒值必须大于 0。");
                }

                return new RateLimitPolicy(1, TimeSpan.FromMilliseconds(minimumIntervalMs));
            }

            var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var maxRequests) &&
                long.TryParse(parts[1], out var windowMs) &&
                maxRequests > 0 &&
                windowMs > 0)
            {
                return new RateLimitPolicy(maxRequests, TimeSpan.FromMilliseconds(windowMs));
            }

            throw new FormatException("concurrentRate 仅支持毫秒值或 N/window 格式。");
        }
    }
}
