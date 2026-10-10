using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class ProviderRequestLimiterTests
{
    private static readonly ProviderId FirstProviderId = ProviderId.New();
    private static readonly ProviderId SecondProviderId = ProviderId.New();

    [Fact]
    public async Task Admission_enforces_count_over_window()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new ProviderRequestLimiter(timeProvider);

        await limiter.AdmitAndReleaseAsync(FirstProviderId, new ProviderRequestRateLimit(3, 1000), TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        await limiter.AdmitAndReleaseAsync(FirstProviderId, new ProviderRequestRateLimit(3, 1000), TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        await limiter.AdmitAndReleaseAsync(FirstProviderId, new ProviderRequestRateLimit(3, 1000), TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);

        var fourthRequest = limiter.AdmitAndReleaseAsync(
            FirstProviderId,
            new ProviderRequestRateLimit(3, 1000),
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);
        await AssertPendingAsync(fourthRequest);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1000));
        await fourthRequest;
    }

    [Fact]
    public async Task Admission_isolates_state_per_provider()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new ProviderRequestLimiter(timeProvider);

        await limiter.AdmitAndReleaseAsync(FirstProviderId, new ProviderRequestRateLimit(1, 1000), TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        var blockedProvider = limiter.AdmitAndReleaseAsync(
            FirstProviderId,
            new ProviderRequestRateLimit(1, 1000),
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);
        await limiter.AdmitAndReleaseAsync(SecondProviderId, new ProviderRequestRateLimit(1, 1000), TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);

        await AssertPendingAsync(blockedProvider);
    }

    [Fact]
    public async Task ApplyRetryAfter_extends_wait_window()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new ProviderRequestLimiter(timeProvider);

        await limiter.AdmitAndReleaseAsync(FirstProviderId, new ProviderRequestRateLimit(1, 1000), TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        limiter.ApplyRetryAfter(FirstProviderId, TimeSpan.FromSeconds(3));

        var retriedRequest = limiter.AdmitAndReleaseAsync(
            FirstProviderId,
            new ProviderRequestRateLimit(1, 1000),
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);
        await AssertPendingAsync(retriedRequest);

        timeProvider.Advance(TimeSpan.FromSeconds(2.9));
        await AssertPendingAsync(retriedRequest);

        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await retriedRequest;
    }

    [Fact]
    public async Task Admission_admits_current_then_prefetch_then_active_cache()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new ProviderRequestLimiter(timeProvider);

        await limiter.AdmitAndReleaseAsync(FirstProviderId, new ProviderRequestRateLimit(1, 100), TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        var activeCache = limiter.AdmitAndReleaseAsync(
            FirstProviderId,
            new ProviderRequestRateLimit(1, 100),
            TtsAdmissionPriority.ActiveCache,
            CancellationToken.None);
        var prefetch = limiter.AdmitAndReleaseAsync(
            FirstProviderId,
            new ProviderRequestRateLimit(1, 100),
            TtsAdmissionPriority.Prefetch,
            CancellationToken.None);
        var current = limiter.AdmitAndReleaseAsync(
            FirstProviderId,
            new ProviderRequestRateLimit(1, 100),
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);

        await timeProvider.WaitForPendingTimerCountAsync(1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await current;
        Assert.False(prefetch.IsCompleted);
        Assert.False(activeCache.IsCompleted);

        await timeProvider.WaitForPendingTimerCountAsync(1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await prefetch;
        Assert.False(activeCache.IsCompleted);

        await timeProvider.WaitForPendingTimerCountAsync(1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await activeCache;
    }

    [Fact]
    public async Task AcquireAsync_holds_one_shared_execution_lease_per_provider()
    {
        var limiter = new ProviderRequestLimiter(new ManualTimeProvider());
        await using var first = await limiter.AcquireAsync(
            FirstProviderId,
            rateLimit: null,
            TtsAdmissionPriority.ActiveCache,
            CancellationToken.None);
        var second = limiter.AcquireAsync(
            FirstProviderId,
            rateLimit: null,
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);

        await AssertPendingAsync(second);
        await first.DisposeAsync();
        await using var admittedSecond = await second;
    }

    [Fact]
    public async Task Cancelling_a_queued_lease_does_not_take_the_shared_execution_permit()
    {
        var limiter = new ProviderRequestLimiter(new ManualTimeProvider());
        await using var blocker = await limiter.AcquireAsync(
            FirstProviderId,
            rateLimit: null,
            TtsAdmissionPriority.CurrentPlayback,
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var cancelled = limiter.AcquireAsync(
            FirstProviderId,
            rateLimit: null,
            TtsAdmissionPriority.CurrentPlayback,
            cancellation.Token);
        var next = limiter.AcquireAsync(
            FirstProviderId,
            rateLimit: null,
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
        var limiter = new ProviderRequestLimiter(timeProvider);
        using var cancellation = new CancellationTokenSource();

        await limiter.AdmitAndReleaseAsync(FirstProviderId, new ProviderRequestRateLimit(1, 100), TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        var cancelled = limiter.AdmitAndReleaseAsync(FirstProviderId, new ProviderRequestRateLimit(1, 100), TtsAdmissionPriority.CurrentPlayback, cancellation.Token);
        var next = limiter.AdmitAndReleaseAsync(FirstProviderId, new ProviderRequestRateLimit(1, 100), TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await next;
    }

    [Fact]
    public async Task Lower_priority_waiter_is_not_permanently_starved_by_current_playback()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new ProviderRequestLimiter(timeProvider);

        await limiter.AdmitAndReleaseAsync(FirstProviderId, new ProviderRequestRateLimit(1, 100), TtsAdmissionPriority.CurrentPlayback, CancellationToken.None);
        var background = limiter.AdmitAndReleaseAsync(
            FirstProviderId,
            new ProviderRequestRateLimit(1, 100),
            TtsAdmissionPriority.ActiveCache,
            CancellationToken.None);
        var playback = Enumerable.Range(0, 12)
            .Select(_ => limiter.AdmitAndReleaseAsync(
                FirstProviderId,
                new ProviderRequestRateLimit(1, 100),
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

    private static async Task AssertPendingAsync(Task task)
    {
        await Task.Yield();
        Assert.False(task.IsCompleted);
    }
}

internal static class ProviderRequestLimiterTestExtensions
{
    public static async Task AdmitAndReleaseAsync(
        this IProviderRequestLimiter limiter,
        ProviderId providerId,
        ProviderRequestRateLimit? rateLimit,
        TtsAdmissionPriority priority,
        CancellationToken cancellationToken)
    {
        await using var lease = await limiter
            .AcquireAsync(providerId, rateLimit, priority, cancellationToken);
    }
}
