using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shell.Activation;
using Xunit;

namespace NovelSpeaker.App.PresentationTests;

public sealed class LatestOperationSlotTests
{
    [Fact]
    public async Task Replacement_and_page_leave_reject_late_results_and_drain_owned_work()
    {
        using var controller = new PageActivationController();
        var activation = controller.Activate();
        using var slot = new LatestOperationSlot();
        var oldResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = slot.Begin(activation: activation);
        var committed = "initial";
        activation.Register(old.RunAsync(async operation =>
        {
            await oldResult.Task;
            operation.TryCommit(() => committed = "old");
        }));

        var current = slot.Begin(activation: activation);
        Assert.True(old.CancellationToken.IsCancellationRequested);
        Assert.False(activation.CancellationToken.IsCancellationRequested);
        Assert.True(current.Identity > old.Identity);
        Assert.True(current.TryCommit(() => committed = "current"));
        oldResult.SetResult();
        await activation.WaitForPendingOperationsAsync();
        Assert.Equal("current", committed);

        var lateFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reports = 0;
        var unsubscribed = false;
        activation.Register(() => unsubscribed = true);
        activation.Register(current.RunAsync(_ => lateFailure.Task, _ => reports++));
        controller.Deactivate();
        Assert.True(current.CancellationToken.IsCancellationRequested);
        Assert.True(unsubscribed);
        Assert.False(current.TryCommit(() => committed = "late"));
        lateFailure.SetException(new InvalidOperationException("late"));
        await activation.WaitForPendingOperationsAsync();
        Assert.Equal(0, reports);
        Assert.Equal(0, activation.PendingOperationCount);
        slot.Dispose();
        slot.Dispose();
        current.Dispose();
    }

    [Fact]
    public async Task Only_current_failures_are_projected_and_completion_does_not_release_replacement()
    {
        using var slot = new LatestOperationSlot();
        var reports = 0;
        await slot.Begin().RunAsync(_ => Task.FromException(new InvalidOperationException()), _ => reports++);
        Assert.Equal(1, reports);
        Assert.Null(slot.Current);

        var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = slot.Begin();
        var task = old.RunAsync(_ => failure.Task, _ => reports++);
        using var current = slot.Begin();
        failure.SetException(new InvalidOperationException());
        await task;
        Assert.Equal(1, reports);
        Assert.Same(current, slot.Current);
        Assert.True(current.IsCurrent);

        await slot.Begin().RunAsync(_ => Task.FromCanceled(new CancellationToken(true)), _ => reports++);
        Assert.Equal(1, reports);
        Assert.Null(slot.Current);
    }
}
