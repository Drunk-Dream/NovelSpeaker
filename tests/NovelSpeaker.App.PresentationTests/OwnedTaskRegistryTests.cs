using NovelSpeaker.App.Shared.Presentation;
using Xunit;

namespace NovelSpeaker.App.PresentationTests;

public sealed class OwnedTaskRegistryTests
{
    [Fact]
    public async Task Registered_failure_is_observed_and_transferred_to_owner()
    {
        var registry = new OwnedTaskRegistry();
        var observed = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        registry.Register(
            Task.FromException(new InvalidOperationException("operation failed")),
            exception => observed.TrySetResult(exception));

        var exception = await observed.Task;

        Assert.Equal("operation failed", exception.Message);
        Assert.Equal(0, registry.PendingCount);
    }

    [Fact]
    public void Registered_cancellation_is_observed_without_reporting_failure()
    {
        var registry = new OwnedTaskRegistry();
        var failureCount = 0;

        registry.Register(
            Task.FromCanceled(new CancellationToken(canceled: true)),
            _ => Interlocked.Increment(ref failureCount));

        Assert.Equal(0, failureCount);
        Assert.Equal(0, registry.PendingCount);
    }
}
