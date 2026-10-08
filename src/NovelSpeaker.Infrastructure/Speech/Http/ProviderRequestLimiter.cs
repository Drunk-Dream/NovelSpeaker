using System.Collections.Concurrent;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>
/// Owns the priority queue, single execution lease, request pacing, and server backoff per Provider.
/// </summary>
public sealed class ProviderRequestLimiter : IProviderRequestLimiter
{
    private const int MaximumPriorityBypasses = 8;
    private readonly ConcurrentDictionary<ProviderId, ProviderState> _states = new();
    private readonly TimeProvider _timeProvider;
    private long _nextSequence;

    public ProviderRequestLimiter(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task<ITtsAdmissionLease> AcquireAsync(
        ProviderId providerId,
        ProviderRequestRateLimit? rateLimit,
        TtsAdmissionPriority priority,
        CancellationToken cancellationToken)
    {
        if (rateLimit is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateLimit.MaxRequests);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateLimit.WindowMilliseconds);
        }

        var policy = rateLimit;
        cancellationToken.ThrowIfCancellationRequested();

        var state = _states.GetOrAdd(providerId, static _ => new ProviderState());
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
                var (providerState, admissionWaiter) = ((ProviderState, AdmissionWaiter))callbackState!;
                CancelWaiter(providerState, admissionWaiter);
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

    public void ApplyRetryAfter(ProviderId providerId, TimeSpan retryAfter)
    {
        if (retryAfter < TimeSpan.Zero)
        {
            retryAfter = TimeSpan.Zero;
        }

        var state = _states.GetOrAdd(providerId, static _ => new ProviderState());
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

    private async Task PumpAsync(ProviderState state)
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

    private static AdmissionWaiter SelectNextWaiter(ProviderState state)
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
        ProviderState state,
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

    private void ReleaseLease(ProviderState state)
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

    private static void CancelWaiter(ProviderState state, AdmissionWaiter waiter)
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

    private static void TrimWindow(ProviderState state, ProviderRequestRateLimit policy, DateTimeOffset now)
    {
        while (state.RequestTimestamps.Count > 0 &&
               now - state.RequestTimestamps.Peek() >= TimeSpan.FromMilliseconds(policy.WindowMilliseconds))
        {
            state.RequestTimestamps.Dequeue();
        }
    }

    private static TimeSpan GetRequiredWait(ProviderState state, ProviderRequestRateLimit? policy, DateTimeOffset now)
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
        var windowWait = (oldest + TimeSpan.FromMilliseconds(policy.WindowMilliseconds)) - now;
        return windowWait > waitTime ? windowWait : waitTime;
    }

    private sealed class ProviderState
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
        ProviderRequestRateLimit? policy,
        TtsAdmissionPriority priority,
        long sequence,
        CancellationToken cancellationToken)
    {
        public ProviderRequestRateLimit? Policy { get; } = policy;

        public TtsAdmissionPriority Priority { get; } = priority;

        public long Sequence { get; } = sequence;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public TaskCompletionSource<ITtsAdmissionLease> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LinkedListNode<AdmissionWaiter>? Node { get; set; }

        public int PriorityBypasses { get; set; }
    }

    private sealed class AdmissionLease(ProviderRequestLimiter owner, ProviderState state) : ITtsAdmissionLease
    {
        private ProviderState? _state = state;

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
}
