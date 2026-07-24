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
        Assert.False(controller.IsActive);
        Assert.False(activation.IsCurrent);
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
