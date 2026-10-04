using NovelSpeaker.Application.Cache.ActiveCache;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Shell;

public sealed class ShellActiveCacheControllerTests
{
    private void Running_snapshot_projects_compact_progress_and_chapter_rows()
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

    private void Snapshot_events_are_dispatched_and_terminal_notification_is_emitted_once_per_batch()
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
        Assert.Equal(("主动缓存完成", "缓存完成：成功 1，跳过 0，失败 0。"), feedback.SuccessMessages[0]);
    }

    private void Cancelled_result_uses_a_clear_warning_message()
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

    private void Failed_result_preserves_application_safe_summary_and_notifies_once_per_batch()
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
            [("主动缓存失败", "缓存完成：成功 1，跳过 0，失败 0。语音服务暂时不可用，请稍后重试。")],
            feedback.WarningMessages);
    }

    private void Failed_result_without_safe_summary_uses_generic_fallback()
    {
        foreach (var errorSummary in new string?[] { null, string.Empty, "   " })
        {
            var coordinator = new FakeActiveCacheCoordinator(CreateSnapshot(ActiveCacheBatchStatus.Running));
            var feedback = new FakeFeedbackService();
            var controller = new ShellActiveCacheController(
                coordinator,
                feedback,
                new InlineUiScheduler());

            coordinator.Publish(CreateSnapshot(ActiveCacheBatchStatus.Failed, errorSummary));

            Assert.Equal(
                [("主动缓存失败", "缓存完成：成功 1，跳过 0，失败 0。主动缓存失败，请重试。")],
                feedback.WarningMessages);
        }
    }

    private async Task Cancel_command_only_forwards_to_process_coordinator()
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

    private void Dispose_detaches_process_snapshot_subscription()
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

    private void Incremental_progress_updates_keep_rows_and_only_notify_the_changed_chapter()
    {
        var initial = CreateSnapshot(ActiveCacheBatchStatus.Running);
        var coordinator = new FakeActiveCacheCoordinator(initial);
        var controller = new ShellActiveCacheController(coordinator,
            new FakeFeedbackService(), new InlineUiScheduler());
        var rows = controller.Chapters.ToArray();
        var changedProperties = new List<string?>();
        rows[1].PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        var collectionChanges = 0;
        controller.Chapters.CollectionChanged += (_, _) => collectionChanges++;

        coordinator.Publish(WithChapterProgress(initial, chapterIndex: 1, completedSegments: 3));
        coordinator.Publish(WithChapterProgress(initial, chapterIndex: 1, completedSegments: 4));

        Assert.Equal([0, 1, 2], controller.Chapters.Select(chapter => chapter.ChapterIndex));
        Assert.Collection(controller.Chapters,
            chapter => Assert.Same(rows[0], chapter),
            chapter => Assert.Same(rows[1], chapter),
            chapter => Assert.Same(rows[2], chapter));
        Assert.Equal("4 / 5", rows[1].StatusText);
        Assert.Equal([nameof(ShellActiveCacheChapterItem.StatusText), nameof(ShellActiveCacheChapterItem.StatusText)],
            changedProperties);
        Assert.Equal(0, collectionChanges);
        Assert.Equal("总进度 6 / 10 段", controller.TotalSegmentProgressText);
    }

    private void Replacing_the_batch_rebuilds_rows_in_snapshot_order()
    {
        var coordinator = new FakeActiveCacheCoordinator(CreateSnapshot(ActiveCacheBatchStatus.Running));
        var controller = new ShellActiveCacheController(coordinator, new FakeFeedbackService(), new InlineUiScheduler());
        var originalRows = controller.Chapters.ToArray();
        var collectionChanges = 0;
        controller.Chapters.CollectionChanged += (_, _) => collectionChanges++;
        var replacement = CreateSnapshot(
            ActiveCacheBatchStatus.Running,
            batchId: Guid.Parse("20000000-0000-0000-0000-000000000002"),
            chapters:
            [
                new ActiveCacheChapterSnapshot(9, "第九章", 0, 1, ActiveCacheChapterStatus.Running, null),
                new ActiveCacheChapterSnapshot(4, "第四章", 1, 1, ActiveCacheChapterStatus.Completed, null)
            ]);

        coordinator.Publish(replacement);

        Assert.Equal([9, 4], controller.Chapters.Select(chapter => chapter.ChapterIndex));
        Assert.All(controller.Chapters, row => Assert.DoesNotContain(row, originalRows));
        Assert.Equal(3, collectionChanges);
    }

    [Fact]
    public void Active_cache_projection_contracts_cover_running_progress_and_terminal_notifications()
    {
        Running_snapshot_projects_compact_progress_and_chapter_rows();
        Incremental_progress_updates_keep_rows_and_only_notify_the_changed_chapter();
        Replacing_the_batch_rebuilds_rows_in_snapshot_order();
        Snapshot_events_are_dispatched_and_terminal_notification_is_emitted_once_per_batch();
    }

    [Fact]
    public void Active_cache_result_contracts_cover_cancellation_failure_summaries_and_fallback()
    {
        Cancelled_result_uses_a_clear_warning_message();
        Failed_result_preserves_application_safe_summary_and_notifies_once_per_batch();
        Failed_result_without_safe_summary_uses_generic_fallback();
    }

    [Fact]
    public async Task Active_cache_command_contracts_cover_cancel_forwarding_and_disposal()
    {
        await Cancel_command_only_forwards_to_process_coordinator();
        Dispose_detaches_process_snapshot_subscription();
    }

    private static ActiveCacheSnapshot CreateSnapshot(
        ActiveCacheBatchStatus status,
        string? errorSummary = null,
        Guid? batchId = null,
        IReadOnlyList<ActiveCacheChapterSnapshot>? chapters = null) =>
        new(
            batchId ?? Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "book-1",
            "示例小说",
            status,
            status == ActiveCacheBatchStatus.Completed ? 3 : 1,
            3,
            status == ActiveCacheBatchStatus.Completed ? 10 : 4,
            10,
            status is ActiveCacheBatchStatus.Completed or ActiveCacheBatchStatus.Cancelled ? null : 1,
            status is ActiveCacheBatchStatus.Completed or ActiveCacheBatchStatus.Cancelled ? null : "第二章",
            chapters ??
            [
                new ActiveCacheChapterSnapshot(0, "第一章", 3, 3, ActiveCacheChapterStatus.Completed, null),
                new ActiveCacheChapterSnapshot(1, "第二章", 2, 5, ActiveCacheChapterStatus.Running, null),
                new ActiveCacheChapterSnapshot(2, "第三章", 0, 2, ActiveCacheChapterStatus.Pending, null)
            ],
            errorSummary);

    private static ActiveCacheSnapshot WithChapterProgress(
        ActiveCacheSnapshot snapshot,
        int chapterIndex,
        int completedSegments)
    {
        var previous = snapshot.Chapters.Single(chapter => chapter.ChapterIndex == chapterIndex);
        var chapters = snapshot.Chapters
            .Select(chapter => chapter.ChapterIndex == chapterIndex
                ? chapter with { CompletedSegmentCount = completedSegments }
                : chapter)
            .ToArray();
        return snapshot with
        {
            CompletedSegmentCount = snapshot.CompletedSegmentCount + completedSegments - previous.CompletedSegmentCount,
            Chapters = chapters
        };
    }

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
