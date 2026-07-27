using System.Collections.Concurrent;
using NovelSpeaker.Application.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>
/// Owns the priority queue, single execution lease, request pacing, and server backoff per TTS rule.
/// </summary>
public sealed class TtsRateLimiter : ITtsRateLimiter
{
    private const int MaximumPriorityBypasses = 8;
    private readonly ConcurrentDictionary<long, RuleState> _states = new();
    private readonly TimeProvider _timeProvider;
    private long _nextSequence;

    public TtsRateLimiter(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task<ITtsAdmissionLease> AcquireAsync(
        long ruleId,
        string? concurrentRate,
        TtsAdmissionPriority priority,
        CancellationToken cancellationToken)
    {
        var policy = RateLimitPolicy.Parse(concurrentRate);
        cancellationToken.ThrowIfCancellationRequested();

        var state = _states.GetOrAdd(ruleId, static _ => new RuleState());
        var waiter = new AdmissionWaiter(
            policy,
            priority,
            Interlocked.Increment(ref _nextSequence),
            cancellationToken);

        lock (state.SyncRoot)
        {
            waiter.Node = state.Waiters.AddLast(waiter);
            if (!state.PumpRunning && !state.LeaseActive)
            {
                state.PumpRunning = true;
                state.PumpTask = PumpAsync(state);
            }
            else
            {
                state.QueueChanged.TrySetResult();
            }
        }

        using var cancellationRegistration = cancellationToken.Register(
            static callbackState =>
            {
                var (ruleState, admissionWaiter) = ((RuleState, AdmissionWaiter))callbackState!;
                CancelWaiter(ruleState, admissionWaiter);
            },
            (state, waiter));

        try
        {
            return await waiter.Completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    public void ApplyRetryAfter(long ruleId, TimeSpan retryAfter)
    {
        if (retryAfter < TimeSpan.Zero)
        {
            retryAfter = TimeSpan.Zero;
        }

        var state = _states.GetOrAdd(ruleId, static _ => new RuleState());
        lock (state.SyncRoot)
        {
            var blockedUntil = _timeProvider.GetUtcNow() + retryAfter;
            if (blockedUntil > state.BlockedUntilUtc)
            {
                state.BlockedUntilUtc = blockedUntil;
                state.QueueChanged.TrySetResult();
            }
        }
    }

    private async Task PumpAsync(RuleState state)
    {
        try
        {
            while (true)
            {
                AdmissionWaiter? admitted = null;
                Task? waitTask = null;

                lock (state.SyncRoot)
                {
                    if (state.Waiters.Count == 0)
                    {
                        state.PumpRunning = false;
                        state.PumpTask = null;
                        return;
                    }

                    var waiter = SelectNextWaiter(state);
                    var now = _timeProvider.GetUtcNow();
                    if (waiter.Policy is not null)
                    {
                        TrimWindow(state, waiter.Policy, now);
                    }

                    var waitTime = GetRequiredWait(state, waiter.Policy, now);
                    if (waitTime <= TimeSpan.Zero)
                    {
                        state.Waiters.Remove(waiter.Node!);
                        waiter.Node = null;
                        RecordAdmission(state, waiter, now);
                        state.PumpRunning = false;
                        state.PumpTask = null;
                        admitted = waiter;
                    }
                    else
                    {
                        if (state.QueueChanged.Task.IsCompleted)
                        {
                            state.QueueChanged = CreateSignal();
                        }

                        waitTask = WaitForDelayOrQueueChangeAsync(
                            waitTime,
                            state.QueueChanged.Task);
                    }
                }

                if (admitted is not null)
                {
                    admitted.Completion.TrySetResult(new AdmissionLease(this, state));
                    return;
                }

                await waitTask!.ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            AdmissionWaiter[] abandoned;
            lock (state.SyncRoot)
            {
                abandoned = state.Waiters.ToArray();
                state.Waiters.Clear();
                state.PumpRunning = false;
                state.PumpTask = null;
            }

            foreach (var waiter in abandoned)
            {
                waiter.Completion.TrySetException(exception);
            }
        }
    }

    private async Task WaitForDelayOrQueueChangeAsync(
        TimeSpan delay,
        Task queueChanged)
    {
        using var cancellation = new CancellationTokenSource();
        var delayTask = Task.Delay(delay, _timeProvider, cancellation.Token);
        if (await Task.WhenAny(delayTask, queueChanged).ConfigureAwait(false) != delayTask)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await delayTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }

            return;
        }

        await delayTask.ConfigureAwait(false);
    }

    private static AdmissionWaiter SelectNextWaiter(RuleState state)
    {
        var starved = state.Waiters
            .Where(static waiter => waiter.PriorityBypasses >= MaximumPriorityBypasses)
            .OrderBy(static waiter => waiter.Sequence)
            .FirstOrDefault();
        if (starved is not null)
        {
            return starved;
        }

        return state.Waiters
            .OrderByDescending(static waiter => waiter.Priority)
            .ThenBy(static waiter => waiter.Sequence)
            .First();
    }

    private static void RecordAdmission(
        RuleState state,
        AdmissionWaiter admitted,
        DateTimeOffset admittedAt)
    {
        foreach (var waiter in state.Waiters)
        {
            if (waiter.Priority < admitted.Priority)
            {
                waiter.PriorityBypasses++;
            }
        }

        if (admitted.Policy is not null)
        {
            state.RequestTimestamps.Enqueue(admittedAt);
            if (state.RequestTimestamps.Count > admitted.Policy.MaxRequests)
            {
                state.RequestTimestamps.Dequeue();
            }
        }

        if (state.BlockedUntilUtc <= admittedAt)
        {
            state.BlockedUntilUtc = DateTimeOffset.MinValue;
        }

        state.LeaseActive = true;
    }

    private void ReleaseLease(RuleState state)
    {
        lock (state.SyncRoot)
        {
            if (!state.LeaseActive)
            {
                return;
            }

            state.LeaseActive = false;
            if (state.Waiters.Count > 0 && !state.PumpRunning)
            {
                state.PumpRunning = true;
                state.PumpTask = PumpAsync(state);
            }
        }
    }

    private static void CancelWaiter(RuleState state, AdmissionWaiter waiter)
    {
        var removed = false;
        lock (state.SyncRoot)
        {
            if (waiter.Node?.List is not null)
            {
                state.Waiters.Remove(waiter.Node);
                waiter.Node = null;
                removed = true;
                state.QueueChanged.TrySetResult();
            }
        }

        if (removed)
        {
            waiter.Completion.TrySetCanceled(waiter.CancellationToken);
        }
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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
        public object SyncRoot { get; } = new();

        public LinkedList<AdmissionWaiter> Waiters { get; } = [];

        public Queue<DateTimeOffset> RequestTimestamps { get; } = new();

        public DateTimeOffset BlockedUntilUtc { get; set; } = DateTimeOffset.MinValue;

        public bool PumpRunning { get; set; }

        public Task? PumpTask { get; set; }

        public bool LeaseActive { get; set; }

        public TaskCompletionSource QueueChanged { get; set; } = CreateSignal();
    }

    private sealed class AdmissionWaiter(
        RateLimitPolicy? policy,
        TtsAdmissionPriority priority,
        long sequence,
        CancellationToken cancellationToken)
    {
        public RateLimitPolicy? Policy { get; } = policy;

        public TtsAdmissionPriority Priority { get; } = priority;

        public long Sequence { get; } = sequence;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public TaskCompletionSource<ITtsAdmissionLease> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LinkedListNode<AdmissionWaiter>? Node { get; set; }

        public int PriorityBypasses { get; set; }
    }

    private sealed class AdmissionLease(TtsRateLimiter owner, RuleState state) : ITtsAdmissionLease
    {
        private RuleState? _state = state;

        public ValueTask DisposeAsync()
        {
            var stateToRelease = Interlocked.Exchange(ref _state, null);
            if (stateToRelease is not null)
            {
                owner.ReleaseLease(stateToRelease);
            }

            return ValueTask.CompletedTask;
        }
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
