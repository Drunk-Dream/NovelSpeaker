using NovelSpeaker.Application.Speech;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class TtsRateLimiterTests
{
    [Fact]
    public async Task WaitAsync_parses_minimum_interval_format()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);

        await limiter.WaitAsync(1, "1000", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        var secondRequest = limiter.WaitAsync(
            1,
            "1000",
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);

        await AssertPendingAsync(secondRequest);
        timeProvider.Advance(TimeSpan.FromMilliseconds(999));
        await AssertPendingAsync(secondRequest);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await secondRequest;
    }

    [Fact]
    public async Task WaitAsync_parses_count_over_window_format()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);

        await limiter.WaitAsync(1, "3/1000", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        await limiter.WaitAsync(1, "3/1000", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        await limiter.WaitAsync(1, "3/1000", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);

        var fourthRequest = limiter.WaitAsync(
            1,
            "3/1000",
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);
        await AssertPendingAsync(fourthRequest);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1000));
        await fourthRequest;
    }

    [Fact]
    public async Task WaitAsync_isolates_state_per_rule()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);

        await limiter.WaitAsync(1, "1000", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        var blockedRule = limiter.WaitAsync(
            1,
            "1000",
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);
        await limiter.WaitAsync(2, "1000", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);

        await AssertPendingAsync(blockedRule);
    }

    [Fact]
    public async Task ApplyRetryAfter_extends_wait_window()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);

        await limiter.WaitAsync(1, "1000", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        limiter.ApplyRetryAfter(1, TimeSpan.FromSeconds(3));

        var retriedRequest = limiter.WaitAsync(
            1,
            "1000",
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);
        await AssertPendingAsync(retriedRequest);

        timeProvider.Advance(TimeSpan.FromSeconds(2.9));
        await AssertPendingAsync(retriedRequest);

        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await retriedRequest;
    }

    [Fact]
    public async Task WaitAsync_honors_cancellation()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);
        using var cts = new CancellationTokenSource();

        await limiter.WaitAsync(1, "1000", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        var pending = limiter.WaitAsync(1, "1000", TtsAdmissionPriority.CurrentPlayback, cts.Token);

        await AssertPendingAsync(pending);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public async Task WaitAsync_admits_concurrent_equal_priority_callers_in_fifo_order()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);

        await limiter.WaitAsync(1, "100", TtsAdmissionPriority.Prefetch, CancellationToken.None);
        var second = limiter.WaitAsync(1, "100", TtsAdmissionPriority.Prefetch, CancellationToken.None);
        var third = limiter.WaitAsync(1, "100", TtsAdmissionPriority.Prefetch, CancellationToken.None);

        await AssertPendingAsync(second);
        await AssertPendingAsync(third);

        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await second;
        await AssertPendingAsync(third);

        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await third;
    }

    [Fact]
    public async Task WaitAsync_admits_current_then_prefetch_then_active_cache()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);

        await limiter.WaitAsync(1, "100", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        var activeCache = limiter.WaitAsync(
            1,
            "100",
            TtsAdmissionPriority.ActiveCache,
            CancellationToken.None);
        var prefetch = limiter.WaitAsync(
            1,
            "100",
            TtsAdmissionPriority.Prefetch,
            CancellationToken.None);
        var current = limiter.WaitAsync(
            1,
            "100",
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);

        Assert.Equal(1, timeProvider.PendingTimerCount);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await current;
        Assert.False(prefetch.IsCompleted);
        Assert.False(activeCache.IsCompleted);

        Assert.Equal(1, timeProvider.PendingTimerCount);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await prefetch;
        Assert.False(activeCache.IsCompleted);

        Assert.Equal(1, timeProvider.PendingTimerCount);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await activeCache;
    }

    [Fact]
    public async Task AcquireAsync_holds_one_shared_execution_lease_per_rule()
    {
        var limiter = new TtsRateLimiter(new ManualTimeProvider());
        await using var first = await limiter.AcquireAsync(
            1,
            concurrentRate: null,
            TtsAdmissionPriority.ActiveCache,
            CancellationToken.None);
        var second = limiter.AcquireAsync(
            1,
            concurrentRate: null,
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);

        await AssertPendingAsync(second);
        await first.DisposeAsync();
        await using var admittedSecond = await second;
    }

    [Fact]
    public async Task Cancelling_a_queued_lease_does_not_take_the_shared_execution_permit()
    {
        var limiter = new TtsRateLimiter(new ManualTimeProvider());
        await using var blocker = await limiter.AcquireAsync(
            1,
            concurrentRate: null,
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var cancelled = limiter.AcquireAsync(
            1,
            concurrentRate: null,
            TtsAdmissionPriority.CurrentPlayback,
            cancellation.Token);
        var next = limiter.AcquireAsync(
            1,
            concurrentRate: null,
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);
        await blocker.DisposeAsync();
        await using var admittedNext = await next;
    }

    [Fact]
    public async Task Cancelling_a_waiter_does_not_consume_rate_quota()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);
        using var cancellation = new CancellationTokenSource();

        await limiter.WaitAsync(1, "100", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        var cancelled = limiter.WaitAsync(1, "100", TtsAdmissionPriority.CurrentPlayback, cancellation.Token);
        var next = limiter.WaitAsync(1, "100", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await next;
    }

    [Fact]
    public async Task Lower_priority_waiter_is_not_permanently_starved_by_current_playback()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);

        await limiter.WaitAsync(1, "100", TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        var background = limiter.WaitAsync(
            1,
            "100",
            TtsAdmissionPriority.ActiveCache,
            CancellationToken.None);
        var playback = Enumerable.Range(0, 12)
            .Select(_ => limiter.WaitAsync(
                1,
                "100",
                TtsAdmissionPriority.CurrentPlayback,
                CancellationToken.None))
            .ToArray();

        await AssertPendingAsync(background);
        await timeProvider.WaitForPendingTimerCountAsync(1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await playback[0];
        Assert.False(background.IsCompleted);

        for (var admission = 1; admission <= 8 && !background.IsCompleted; admission++)
        {
            await timeProvider.WaitForPendingTimerCountAsync(1);
            timeProvider.Advance(TimeSpan.FromMilliseconds(100));
            var admitted = await Task.WhenAny(background, playback[admission]);
            if (ReferenceEquals(admitted, background))
            {
                break;
            }

            await playback[admission];
        }

        await background;
    }

    [Fact]
    public async Task New_waiter_wakes_an_existing_policy_delay_and_recomputes_admission()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);

        await limiter.WaitAsync(
            1,
            "1000",
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);
        var oldWaiter = limiter.WaitAsync(
            1,
            "1000",
            TtsAdmissionPriority.ActiveCache,
            CancellationToken.None);
        await AssertPendingAsync(oldWaiter);
        Assert.Equal(1, timeProvider.PendingTimerCount);

        var newWaiter = limiter.WaitAsync(
            1,
            concurrentRate: null,
            priority: TtsAdmissionPriority.CurrentPlayback,
            cancellationToken: CancellationToken.None);

        await newWaiter.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(oldWaiter.IsCompleted);
        Assert.Equal(1, timeProvider.PendingTimerCount);
        Assert.Equal(1, timeProvider.PendingTimerCount);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await oldWaiter;
        Assert.Equal(0, timeProvider.PendingTimerCount);
    }

    private static async Task AssertPendingAsync(Task task)
    {
        await Task.Yield();
        Assert.False(task.IsCompleted);
    }

}

internal static class TtsRateLimiterTestExtensions
{
    public static async Task WaitAsync(
        this TtsRateLimiter limiter,
        long ruleId,
        string? concurrentRate,
        TtsAdmissionPriority priority,
        CancellationToken cancellationToken)
    {
        await using var lease = await limiter
            .AcquireAsync(ruleId, concurrentRate, priority, cancellationToken);
    }
}
