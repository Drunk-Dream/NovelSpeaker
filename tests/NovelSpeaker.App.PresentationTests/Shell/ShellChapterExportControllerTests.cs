using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Shell;

public sealed class ShellChapterExportControllerTests
{
    [Fact]
    public void Running_snapshot_projects_shell_progress_and_cancel_action()
    {
        var coordinator = new FakeCoordinator(CreateSnapshot(ChapterExportBatchStatus.Running));
        var controller = new ShellChapterExportController(
            coordinator,
            new FakeFeedbackService(),
            new FakeLauncher(),
            new InlineUiScheduler());

        Assert.True(controller.IsVisible);
        Assert.Equal("导出中 · 2/7 章 · 29%", controller.CompactStatusText);
        Assert.Equal("总进度 2 / 7 章", controller.ProgressText);
        Assert.Equal("第 3 章 · 第三章", controller.CurrentChapterText);
        Assert.True(controller.CanCancel);
        Assert.False(controller.CanDismiss);
    }

    [Fact]
    public async Task Completed_snapshot_remains_visible_until_open_directory_or_close()
    {
        var coordinator = new FakeCoordinator(CreateSnapshot(ChapterExportBatchStatus.Running));
        var feedback = new FakeFeedbackService();
        var launcher = new FakeLauncher();
        var controller = new ShellChapterExportController(
            coordinator,
            feedback,
            launcher,
            new InlineUiScheduler());
        var completed = CreateSnapshot(ChapterExportBatchStatus.Completed, completedCount: 7);

        coordinator.Publish(completed);

        Assert.True(controller.IsVisible);
        Assert.Equal("导出完成 · 7 章", controller.CompactStatusText);
        Assert.True(controller.CanOpenDirectory);
        Assert.True(controller.CanDismiss);
        Assert.Equal([("导出完成", "已导出 7 章")], feedback.SuccessMessages);

        await controller.OpenDirectoryCommand.ExecuteAsync(null);

        Assert.Equal(completed.ExportDirectoryPath, launcher.LastPath);
        Assert.False(controller.IsVisible);
    }

    [Fact]
    public async Task Opening_an_old_completed_directory_does_not_clear_a_new_batch_projection()
    {
        var coordinator = new FakeCoordinator(CreateSnapshot(ChapterExportBatchStatus.Completed, completedCount: 7));
        var launcher = new BlockingLauncher();
        var controller = new ShellChapterExportController(
            coordinator,
            new FakeFeedbackService(),
            launcher,
            new InlineUiScheduler());

        var openTask = controller.OpenDirectoryCommand.ExecuteAsync(null);
        await launcher.Started.Task;

        coordinator.Publish(CreateSnapshot(ChapterExportBatchStatus.Running, batchId: Guid.Parse("20000000-0000-0000-0000-000000000002")));
        launcher.Release();
        await openTask;

        Assert.True(controller.IsVisible);
        Assert.Equal("导出中 · 2/7 章 · 29%", controller.CompactStatusText);
    }

    [Fact]
    public void Completed_snapshot_can_be_closed_without_opening_directory()
    {
        var coordinator = new FakeCoordinator(CreateSnapshot(ChapterExportBatchStatus.Completed, completedCount: 7));
        var controller = new ShellChapterExportController(
            coordinator,
            new FakeFeedbackService(),
            new FakeLauncher(),
            new InlineUiScheduler());

        Assert.True(controller.IsVisible);
        controller.DismissCommand.Execute(null);

        Assert.False(controller.IsVisible);
    }

    [Theory]
    [InlineData(ChapterExportBatchStatus.Cancelled, "导出已取消")]
    [InlineData(ChapterExportBatchStatus.Failed, "导出失败")]
    public void Cancelled_or_failed_snapshot_notifies_and_does_not_persist_in_shell(
        ChapterExportBatchStatus status,
        string expectedTitle)
    {
        var coordinator = new FakeCoordinator(CreateSnapshot(ChapterExportBatchStatus.Running));
        var feedback = new FakeFeedbackService();
        var controller = new ShellChapterExportController(
            coordinator,
            feedback,
            new FakeLauncher(),
            new InlineUiScheduler());

        coordinator.Publish(CreateSnapshot(status, errorSummary: status == ChapterExportBatchStatus.Failed ? "导出服务失败。" : null));

        Assert.False(controller.IsVisible);
        Assert.Contains(feedback.WarningMessages, item => item.Title == expectedTitle);
    }

    [Fact]
    public async Task Cancel_command_only_forwards_to_process_coordinator()
    {
        var coordinator = new FakeCoordinator(CreateSnapshot(ChapterExportBatchStatus.Running));
        var controller = new ShellChapterExportController(
            coordinator,
            new FakeFeedbackService(),
            new FakeLauncher(),
            new InlineUiScheduler());

        await controller.CancelCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.CancelCallCount);
    }

    private static ChapterExportSnapshot CreateSnapshot(
        ChapterExportBatchStatus status,
        int completedCount = 2,
        Guid? batchId = null,
        string? errorSummary = null) =>
        new(
            batchId ?? Guid.Parse("20000000-0000-0000-0000-000000000001"),
            "book-1",
            "示例小说",
            status,
            7,
            completedCount,
            0,
            status is ChapterExportBatchStatus.Completed or ChapterExportBatchStatus.Cancelled ? null : 2,
            status is ChapterExportBatchStatus.Completed or ChapterExportBatchStatus.Cancelled ? null : "第三章",
            "D:\\Exports",
            status == ChapterExportBatchStatus.Completed ? "D:\\Exports\\示例小说" : null,
            errorSummary);

    private sealed class FakeCoordinator(ChapterExportSnapshot? snapshot) : IChapterExportCoordinator
    {
        public ChapterExportSnapshot? CurrentSnapshot { get; private set; } = snapshot;

        public int CancelCallCount { get; private set; }

        public event EventHandler<ChapterExportSnapshot>? SnapshotChanged;

        public Task<ChapterExportStartResult> StartAsync(StartChapterExportRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancelCallCount++;
            return Task.CompletedTask;
        }

        public Task WaitForCurrentBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Publish(ChapterExportSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public List<(string Title, string Message)> SuccessMessages { get; } = [];

        public List<(string Title, string Message)> WarningMessages { get; } = [];

        public ProjectedUiError Project(Exception exception) =>
            new("操作失败。", UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected) =>
            WarningMessages.Add((title, projected.UserMessage));

        public void ShowSuccess(string title, string message) => SuccessMessages.Add((title, message));

        public void ShowWarning(string title, string message) => WarningMessages.Add((title, message));

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(
            string title,
            string message,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeLauncher : IPresentationLauncher
    {
        public string? LastPath { get; private set; }

        public Task OpenAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPath = path;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingLauncher : IPresentationLauncher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OpenAsync(string path, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return ReleaseSignal.Task.WaitAsync(cancellationToken);
        }

        public void Release() => ReleaseSignal.TrySetResult();
    }

    private sealed class InlineUiScheduler : IUiScheduler
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) => action();
    }
}
