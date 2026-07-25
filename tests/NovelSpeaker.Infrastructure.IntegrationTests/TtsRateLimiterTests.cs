using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.UnitTests.Common;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class TtsRateLimiterTests
{
    [Fact]
    public async Task WaitAsync_parses_minimum_interval_format()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);

        await limiter.WaitAsync(1, "1000", CancellationToken.None);
        var secondRequest = limiter.WaitAsync(1, "1000", CancellationToken.None);

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

        await limiter.WaitAsync(1, "3/1000", CancellationToken.None);
        await limiter.WaitAsync(1, "3/1000", CancellationToken.None);
        await limiter.WaitAsync(1, "3/1000", CancellationToken.None);

        var fourthRequest = limiter.WaitAsync(1, "3/1000", CancellationToken.None);
        await AssertPendingAsync(fourthRequest);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1000));
        await fourthRequest;
    }

    [Fact]
    public async Task WaitAsync_isolates_state_per_rule()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);

        await limiter.WaitAsync(1, "1000", CancellationToken.None);
        var blockedRule = limiter.WaitAsync(1, "1000", CancellationToken.None);
        await limiter.WaitAsync(2, "1000", CancellationToken.None);

        await AssertPendingAsync(blockedRule);
    }

    [Fact]
    public async Task ApplyRetryAfter_extends_wait_window()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new TtsRateLimiter(timeProvider);

        await limiter.WaitAsync(1, "1000", CancellationToken.None);
        limiter.ApplyRetryAfter(1, TimeSpan.FromSeconds(3));

        var retriedRequest = limiter.WaitAsync(1, "1000", CancellationToken.None);
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

        await limiter.WaitAsync(1, "1000", CancellationToken.None);
        var pending = limiter.WaitAsync(1, "1000", cts.Token);

        await AssertPendingAsync(pending);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    private static async Task AssertPendingAsync(Task task)
    {
        await Task.Yield();
        Assert.False(task.IsCompleted);
    }
}
