using NovelSpeaker.Application.Playback.ActiveCache;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Shell;

public sealed class ShellActiveCacheControllerTests
{
    [Fact]
    public void Running_snapshot_projects_compact_progress_and_chapter_rows()
    {
        var coordinator = new FakeActiveCacheCoordinator(CreateSnapshot(ActiveCacheBatchStatus.Running));
        var controller = new ShellActiveCacheController(
            coordinator,
            new FakeFeedbackService(),
            new InlineUiScheduler());

        Assert.True(controller.IsVisible);
        Assert.Equal("缓存中 · 1/3 章 · 40%", controller.CompactStatusText);
        Assert.Equal("总进度 4 / 10 段", controller.TotalSegmentProgressText);
        Assert.Equal(["已完成", "2 / 5", "等待中"], controller.Chapters.Select(chapter => chapter.StatusText));
        Assert.True(controller.CanCancel);
    }

    [Fact]
    public void Snapshot_events_are_dispatched_and_terminal_notification_is_emitted_once_per_batch()
    {
        var coordinator = new FakeActiveCacheCoordinator(CreateSnapshot(ActiveCacheBatchStatus.Running));
        var feedback = new FakeFeedbackService();
        var scheduler = new InlineUiScheduler(checkAccess: false);
        var controller = new ShellActiveCacheController(coordinator, feedback, scheduler);
        var completed = CreateSnapshot(ActiveCacheBatchStatus.Completed);

        coordinator.Publish(completed);
        coordinator.Publish(completed);

        Assert.Equal(2, scheduler.InvokeCount);
        Assert.False(controller.IsVisible);
        Assert.False(controller.IsFlyoutOpen);
        Assert.Single(feedback.SuccessMessages);
        Assert.Equal(("主动缓存完成", "已缓存 3 章。"), feedback.SuccessMessages[0]);
    }

    [Fact]
    public void Cancelled_result_uses_a_clear_warning_message()
    {
        var coordinator = new FakeActiveCacheCoordinator(CreateSnapshot(ActiveCacheBatchStatus.Running));
        var feedback = new FakeFeedbackService();
        var controller = new ShellActiveCacheController(
            coordinator,
            feedback,
            new InlineUiScheduler());

        coordinator.Publish(CreateSnapshot(ActiveCacheBatchStatus.Cancelled));

        Assert.False(controller.IsVisible);
        Assert.Equal([("主动缓存已取消", "已完成的缓存会保留。")], feedback.WarningMessages);
    }

    [Fact]
    public void Failed_result_preserves_application_safe_summary_and_notifies_once_per_batch()
    {
        var coordinator = new FakeActiveCacheCoordinator(CreateSnapshot(ActiveCacheBatchStatus.Running));
        var feedback = new FakeFeedbackService();
        var controller = new ShellActiveCacheController(
            coordinator,
            feedback,
            new InlineUiScheduler());
        var failed = CreateSnapshot(ActiveCacheBatchStatus.Failed, "语音服务暂时不可用，请稍后重试。");

        coordinator.Publish(failed);
        coordinator.Publish(failed);

        Assert.False(controller.IsVisible);
        Assert.Equal(
            [("主动缓存失败", "语音服务暂时不可用，请稍后重试。")],
            feedback.WarningMessages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Failed_result_without_safe_summary_uses_generic_fallback(string? errorSummary)
    {
        var coordinator = new FakeActiveCacheCoordinator(CreateSnapshot(ActiveCacheBatchStatus.Running));
        var feedback = new FakeFeedbackService();
        var controller = new ShellActiveCacheController(
            coordinator,
            feedback,
            new InlineUiScheduler());

        coordinator.Publish(CreateSnapshot(ActiveCacheBatchStatus.Failed, errorSummary));

        Assert.Equal(
            [("主动缓存失败", "主动缓存失败，请重试。")],
            feedback.WarningMessages);
    }

    [Fact]
    public async Task Cancel_command_only_forwards_to_process_coordinator()
    {
        var snapshot = CreateSnapshot(ActiveCacheBatchStatus.Running);
        var coordinator = new FakeActiveCacheCoordinator(snapshot);
        var controller = new ShellActiveCacheController(
            coordinator,
            new FakeFeedbackService(),
            new InlineUiScheduler());

        await controller.CancelCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.CancelCallCount);
        Assert.Same(snapshot, coordinator.CurrentSnapshot);
        Assert.True(controller.IsVisible);
    }

    [Fact]
    public void Dispose_detaches_process_snapshot_subscription()
    {
        var coordinator = new FakeActiveCacheCoordinator(CreateSnapshot(ActiveCacheBatchStatus.Running));
        var controller = new ShellActiveCacheController(
            coordinator,
            new FakeFeedbackService(),
            new InlineUiScheduler());
        Assert.Equal(1, coordinator.SubscriberCount);

        controller.Dispose();
        controller.Dispose();

        Assert.Equal(0, coordinator.SubscriberCount);
    }

    private static ActiveCacheSnapshot CreateSnapshot(
        ActiveCacheBatchStatus status,
        string? errorSummary = null) =>
        new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "book-1",
            "示例小说",
            status,
            status == ActiveCacheBatchStatus.Completed ? 3 : 1,
            3,
            status == ActiveCacheBatchStatus.Completed ? 10 : 4,
            10,
            status is ActiveCacheBatchStatus.Completed or ActiveCacheBatchStatus.Cancelled ? null : 1,
            status is ActiveCacheBatchStatus.Completed or ActiveCacheBatchStatus.Cancelled ? null : "第二章",
            [
                new ActiveCacheChapterSnapshot(0, "第一章", 3, 3, ActiveCacheChapterStatus.Completed, null),
                new ActiveCacheChapterSnapshot(1, "第二章", 2, 5, ActiveCacheChapterStatus.Running, null),
                new ActiveCacheChapterSnapshot(2, "第三章", 0, 2, ActiveCacheChapterStatus.Pending, null)
            ],
            errorSummary);

    private sealed class FakeActiveCacheCoordinator(ActiveCacheSnapshot? snapshot) : IActiveCacheCoordinator
    {
        private EventHandler<ActiveCacheSnapshot>? _snapshotChanged;

        public ActiveCacheSnapshot? CurrentSnapshot { get; private set; } = snapshot;

        public int CancelCallCount { get; private set; }

        public int SubscriberCount => _snapshotChanged?.GetInvocationList().Length ?? 0;

        public event EventHandler<ActiveCacheSnapshot>? SnapshotChanged
        {
            add => _snapshotChanged += value;
            remove => _snapshotChanged -= value;
        }

        public Task<ActiveCacheStartResult> StartAsync(
            StartActiveCacheRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancelCallCount++;
            return Task.CompletedTask;
        }

        public Task WaitForCurrentBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Publish(ActiveCacheSnapshot next)
        {
            CurrentSnapshot = next;
            _snapshotChanged?.Invoke(this, next);
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public List<(string Title, string Message)> SuccessMessages { get; } = [];

        public List<(string Title, string Message)> WarningMessages { get; } = [];

        public ProjectedUiError Project(Exception exception) =>
            new("操作失败。", UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            WarningMessages.Add((title, projected.UserMessage));
        }

        public void ShowSuccess(string title, string message) => SuccessMessages.Add((title, message));

        public void ShowWarning(string title, string message) => WarningMessages.Add((title, message));

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(
            string title,
            string message,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InlineUiScheduler(bool checkAccess = true) : IUiScheduler
    {
        public int InvokeCount { get; private set; }

        public bool CheckAccess() => checkAccess;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            InvokeCount++;
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            InvokeCount++;
            return action();
        }
    }
}
