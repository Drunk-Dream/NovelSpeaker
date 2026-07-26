using NovelSpeaker.App.Shell.Activation;
using Xunit;

namespace NovelSpeaker.UnitTests.Navigation;

public sealed class PageActivationControllerTests
{
    [Fact]
    public void Rapid_reentry_cancels_old_activation_before_unregistering_page_resources()
    {
        using var controller = new PageActivationController();
        var oldActivation = controller.Activate();
        var cancellationObservedByCleanup = false;
        oldActivation.Register(() =>
        {
            cancellationObservedByCleanup = oldActivation.CancellationToken.IsCancellationRequested;
        });

        var newActivation = controller.Activate();

        Assert.True(oldActivation.CancellationToken.IsCancellationRequested);
        Assert.True(cancellationObservedByCleanup);
        Assert.True(newActivation.IsCurrent);
        Assert.True(newActivation.Version > oldActivation.Version);
    }

    [Fact]
    public async Task Late_result_from_old_activation_cannot_commit_into_new_activation()
    {
        using var controller = new PageActivationController();
        var completion = new TaskCompletionSource<string>();
        var oldActivation = controller.Activate();
        var projection = "initial";
        var oldOperation = CompleteAsync(oldActivation, completion.Task, value => projection = value);

        var newActivation = controller.Activate();
        Assert.True(newActivation.TryCommit(() => projection = "new"));

        completion.SetResult("old");
        await oldOperation;

        Assert.Equal("new", projection);
    }

    [Fact]
    public void Leaving_page_cancels_operations_unregisters_guard_and_releases_scope()
    {
        using var controller = new PageActivationController();
        var activation = controller.Activate();
        var cleanupCount = 0;
        activation.Register(() =>
        {
            Assert.True(activation.CancellationToken.IsCancellationRequested);
            cleanupCount++;
        });
        activation.Register(() =>
        {
            Assert.True(activation.CancellationToken.IsCancellationRequested);
            cleanupCount++;
        });

        controller.Deactivate();

        Assert.Equal(2, cleanupCount);
        Assert.Null(controller.Current);
        Assert.False(activation.IsCurrent);
    }

    [Fact]
    public async Task Registered_page_operation_forwards_failure_while_activation_is_current()
    {
        using var controller = new PageActivationController();
        var activation = controller.Activate();
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        activation.Run(
            _ => Task.FromException(new InvalidOperationException("page operation failed")),
            exception => failure.TrySetResult(exception));

        var exception = await failure.Task;

        Assert.Equal("page operation failed", exception.Message);
        Assert.Equal(0, activation.PendingOperationCount);
    }

    [Fact]
    public async Task Registered_page_operation_observes_but_does_not_forward_late_failure()
    {
        using var controller = new PageActivationController();
        var activation = controller.Activate();
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failureCount = 0;

        activation.Register(operation.Task, _ => Interlocked.Increment(ref failureCount));
        controller.Deactivate();
        operation.SetException(new InvalidOperationException("late page operation failed"));

        await activation.WaitForPendingOperationsAsync();

        Assert.Equal(0, failureCount);
        Assert.Equal(0, activation.PendingOperationCount);
    }

    [Fact]
    public async Task Registered_page_operation_treats_activation_cancellation_as_normal_control_flow()
    {
        using var controller = new PageActivationController();
        var activation = controller.Activate();
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failureCount = 0;

        activation.Run(
            async cancellationToken =>
            {
                operationStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    operationFinished.SetResult();
                }
            },
            _ => Interlocked.Increment(ref failureCount));

        await operationStarted.Task;
        controller.Deactivate();
        await operationFinished.Task;

        Assert.Equal(0, failureCount);
        Assert.Equal(0, activation.PendingOperationCount);
    }

    private static async Task CompleteAsync(
        PageActivationScope activation,
        Task<string> result,
        Action<string> commit)
    {
        var value = await result;
        activation.TryCommit(() => commit(value));
    }
}
