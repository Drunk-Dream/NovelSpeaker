using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Activation;
using Xunit;

namespace NovelSpeaker.UnitTests.Navigation;

public sealed class PageEventOperationRunnerTests
{
    [Fact]
    public async Task Current_page_event_failure_is_projected_through_the_shared_entry_boundary()
    {
        var feedback = new RecordingFeedbackService();
        var runner = new PageEventOperationRunner(feedback);
        using var activationController = new PageActivationController();
        activationController.Activate();

        await runner.RunAsync(
            activationController,
            "保存失败",
            _ => Task.FromException(new IOException("private path")));

        Assert.Equal("保存失败", Assert.Single(feedback.Notifications).Title);
        Assert.IsType<IOException>(Assert.Single(feedback.ProjectedExceptions));
    }

    [Fact]
    public async Task Late_page_event_failure_is_observed_without_projecting_into_new_activation()
    {
        var feedback = new RecordingFeedbackService();
        var runner = new PageEventOperationRunner(feedback);
        using var activationController = new PageActivationController();
        activationController.Activate();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = runner.RunAsync(
            activationController,
            "保存失败",
            async _ =>
            {
                await gate.Task;
                throw new InvalidOperationException("late failure");
            });

        activationController.Activate();
        gate.SetResult();
        await operation;

        Assert.Empty(feedback.ProjectedExceptions);
        Assert.Empty(feedback.Notifications);
    }

    private sealed class RecordingFeedbackService : IAppFeedbackService
    {
        public List<Exception> ProjectedExceptions { get; } = [];

        public List<(string Title, ProjectedUiError Projected)> Notifications { get; } = [];

        public ProjectedUiError Project(Exception exception)
        {
            ProjectedExceptions.Add(exception);
            return new ProjectedUiError("安全错误", UiMessageSeverity.Error, false);
        }

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            Notifications.Add((title, projected));
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(
            string title,
            string message,
            CancellationToken cancellationToken) =>
            Task.FromResult(AppConfirmationDecision.Cancel);
    }
}
