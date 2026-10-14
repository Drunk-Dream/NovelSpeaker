using System.Collections.Concurrent;
using NovelSpeaker.Application.Cache.Export;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class ChapterExportCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Throwing_observer_does_not_prevent_worker_progress_terminal_result_or_retry(bool fail)
    {
        var exporter = new ControlledExportService();
        await using var coordinator = new ChapterExportCoordinator(exporter);
        var snapshots = new ConcurrentQueue<ChapterExportSnapshot>();
        coordinator.SnapshotChanged += (_, _) => throw new InvalidOperationException("fixture observer failure");
        coordinator.SnapshotChanged += (_, snapshot) => snapshots.Enqueue(snapshot);

        var start = await coordinator.StartAsync(CreateRequest(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ChapterExportStartStatus.Accepted, start.Status);
        await exporter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        exporter.Report(new ExportChaptersProgress(1, 2, 1));
        if (fail) exporter.Fail(new IOException("fixture worker failure"));
        else exporter.Complete(SucceededResult());
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        var terminal = fail ? ChapterExportBatchStatus.Failed : ChapterExportBatchStatus.Completed;
        Assert.Equal(terminal, coordinator.CurrentSnapshot!.Status);
        Assert.Contains(snapshots, snapshot => snapshot.Status == ChapterExportBatchStatus.Waiting);
        Assert.Contains(snapshots, snapshot => snapshot.Status == ChapterExportBatchStatus.Running &&
            snapshot.CompletedChapterCount == 1);
        Assert.Contains(snapshots, snapshot => snapshot.Status == terminal);
        if (fail) Assert.Equal("章节导出失败，请重试。", coordinator.CurrentSnapshot.ErrorSummary);

        exporter.Reset();
        var retry = await coordinator.StartAsync(CreateRequest(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await exporter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        exporter.Complete(SucceededResult());
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ChapterExportStartStatus.Accepted, retry.Status);
        Assert.NotEqual(start.BatchId, retry.BatchId);
        Assert.Equal(ChapterExportBatchStatus.Completed, coordinator.CurrentSnapshot.Status);
    }

    [Fact]
    public async Task Throwing_observer_does_not_block_duplicate_rejection_cancel_restart_or_dispose()
    {
        var exporter = new ControlledExportService();
        await using var coordinator = new ChapterExportCoordinator(exporter);
        var snapshots = new ConcurrentQueue<ChapterExportSnapshot>();
        coordinator.SnapshotChanged += (_, _) => throw new InvalidOperationException("fixture observer failure");
        coordinator.SnapshotChanged += (_, snapshot) => snapshots.Enqueue(snapshot);

        var start = await coordinator.StartAsync(CreateRequest(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = await coordinator.StartAsync(CreateRequest(), CancellationToken.None);
        await exporter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ChapterExportStartStatus.Accepted, start.Status);
        Assert.Equal(ChapterExportStartStatus.BatchAlreadyActive, duplicate.Status);
        exporter.Report(new ExportChaptersProgress(1, 2, 1));
        await coordinator.CancelAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ChapterExportBatchStatus.Cancelled, coordinator.CurrentSnapshot!.Status);
        Assert.Equal(1, coordinator.CurrentSnapshot.CompletedChapterCount);
        Assert.Contains(snapshots, snapshot => snapshot.Status == ChapterExportBatchStatus.Cancelling);
        Assert.Contains(snapshots, snapshot => snapshot.Status == ChapterExportBatchStatus.Cancelled);

        exporter.Reset();
        var replacement = await coordinator.StartAsync(CreateRequest(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await exporter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ChapterExportStartStatus.Accepted, replacement.Status);
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ChapterExportBatchStatus.Cancelled, coordinator.CurrentSnapshot.Status);
        Assert.Contains(snapshots, snapshot => snapshot.BatchId == replacement.BatchId &&
            snapshot.Status == ChapterExportBatchStatus.Cancelled);
    }

    [Fact]
    public async Task StartAsync_owns_background_batch_and_publishes_chapter_progress()
    {
        var exporter = new ControlledExportService();
        await using var coordinator = new ChapterExportCoordinator(exporter);
        var snapshots = new ConcurrentQueue<ChapterExportSnapshot>();
        coordinator.SnapshotChanged += (_, snapshot) => snapshots.Enqueue(snapshot);

        var start = await coordinator.StartAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.Equal(ChapterExportStartStatus.Accepted, start.Status);
        await exporter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ChapterExportBatchStatus.Running, coordinator.CurrentSnapshot?.Status);

        exporter.Report(new ExportChaptersProgress(0, 2, 0));
        Assert.Equal(0, coordinator.CurrentSnapshot?.CompletedChapterCount);
        Assert.Equal("第一章", coordinator.CurrentSnapshot?.CurrentChapterTitle);

        exporter.Report(new ExportChaptersProgress(1, 2, 0));
        exporter.Report(new ExportChaptersProgress(1, 2, 1));
        Assert.Equal(1, coordinator.CurrentSnapshot?.CompletedChapterCount);
        Assert.Equal("第二章", coordinator.CurrentSnapshot?.CurrentChapterTitle);
        exporter.Report(new ExportChaptersProgress(0, 2, 0));
        Assert.Equal(1, coordinator.CurrentSnapshot?.CompletedChapterCount);

        exporter.Complete(new ExportChaptersResult(
            ExportChaptersStatus.Succeeded,
            @"D:\Export\第一本",
            [
                new ExportedChapterMp3(0, @"D:\Export\第一本\001_第一章.mp3"),
                new ExportedChapterMp3(1, @"D:\Export\第一本\002_第二章.mp3")
            ],
            null));
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);

        var completed = Assert.IsType<ChapterExportSnapshot>(coordinator.CurrentSnapshot);
        Assert.Equal(ChapterExportBatchStatus.Completed, completed.Status);
        Assert.Equal(2, completed.CompletedChapterCount);
        Assert.Equal(@"D:\Export\第一本", completed.ExportDirectoryPath);
        Assert.Contains(snapshots, snapshot => snapshot.Status == ChapterExportBatchStatus.Waiting);
        Assert.Contains(snapshots, snapshot => snapshot.Status == ChapterExportBatchStatus.Running);
    }

    [Fact]
    public async Task StartAsync_rejects_duplicate_active_batch()
    {
        var exporter = new ControlledExportService();
        await using var coordinator = new ChapterExportCoordinator(exporter);

        var first = await coordinator.StartAsync(CreateRequest(), CancellationToken.None);
        await exporter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = await coordinator.StartAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(ChapterExportStartStatus.Accepted, first.Status);
        Assert.Equal(ChapterExportStartStatus.BatchAlreadyActive, duplicate.Status);

        exporter.Complete(SucceededResult());
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_freezes_book_and_chapter_names_for_the_background_request()
    {
        var exporter = new ControlledExportService();
        await using var coordinator = new ChapterExportCoordinator(exporter);

        await coordinator.StartAsync(CreateRequest(), CancellationToken.None);
        await exporter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("第一本", exporter.LastRequest?.FrozenBookTitle);
        Assert.Equal(
            new Dictionary<int, string>
            {
                [0] = "第一章",
                [1] = "第二章"
            },
            exporter.LastRequest?.FrozenChapterTitles);

        exporter.Complete(SucceededResult());
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CancelAsync_cancels_owned_operation_and_publishes_cancelled()
    {
        var exporter = new ControlledExportService { WaitForCancellation = true };
        await using var coordinator = new ChapterExportCoordinator(exporter);
        await coordinator.StartAsync(CreateRequest(), CancellationToken.None);
        await exporter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await coordinator.CancelAsync(CancellationToken.None);

        Assert.True(exporter.ObservedCancellation);
        Assert.Equal(ChapterExportBatchStatus.Cancelled, coordinator.CurrentSnapshot?.Status);
    }

    [Fact]
    public async Task Failed_export_is_projected_to_safe_terminal_snapshot()
    {
        var exporter = new ControlledExportService();
        await using var coordinator = new ChapterExportCoordinator(exporter);
        await coordinator.StartAsync(CreateRequest(), CancellationToken.None);
        await exporter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        exporter.Complete(ExportChaptersResult.Failed(ExportChaptersStatus.IncompleteCache, 1));
        await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);

        var snapshot = Assert.IsType<ChapterExportSnapshot>(coordinator.CurrentSnapshot);
        Assert.Equal(ChapterExportBatchStatus.Failed, snapshot.Status);
        Assert.Equal(1, snapshot.CurrentChapterIndex);
        Assert.Contains("缓存已发生变化", snapshot.ErrorSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chapter_export_failure_uses_chapter_title_instead_of_generated_number()
    {
        foreach (var (status, reason) in new[]
                 {
                     (ExportChaptersStatus.ChapterHasNoPlayableSegments, "没有可播放段落"),
                     (ExportChaptersStatus.ChapterSpeechPlanUnavailable, "章节朗读清单尚未就绪")
                 })
        {
            var exporter = new ControlledExportService();
            await using var coordinator = new ChapterExportCoordinator(exporter);
            await coordinator.StartAsync(CreateRequest(), CancellationToken.None);
            await exporter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            exporter.Complete(ExportChaptersResult.Failed(status, 1));
            await coordinator.WaitForCurrentBatchAsync(CancellationToken.None);

            var snapshot = Assert.IsType<ChapterExportSnapshot>(coordinator.CurrentSnapshot);
            Assert.Equal($"章节“第二章”{reason}。", snapshot.ErrorSummary);
        }
    }

    private static StartChapterExportRequest CreateRequest() =>
        new(
            "book-1",
            "第一本",
            [
                new ChapterExportSelection(0, "第一章"),
                new ChapterExportSelection(1, "第二章")
            ],
            @"D:\Export",
            1);

    private static ExportChaptersResult SucceededResult() =>
        new(
            ExportChaptersStatus.Succeeded,
            @"D:\Export\第一本",
            [
                new ExportedChapterMp3(0, @"D:\Export\第一本\001_第一章.mp3"),
                new ExportedChapterMp3(1, @"D:\Export\第一本\002_第二章.mp3")
            ],
            null);

    private sealed class ControlledExportService : IExportChaptersService
    {
        private TaskCompletionSource<ExportChaptersResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IProgress<ExportChaptersProgress>? _progress;

        public bool WaitForCancellation { get; init; }

        public bool ObservedCancellation { get; private set; }

        public ExportChaptersRequest? LastRequest { get; private set; }

        public TaskCompletionSource Started { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ExportChaptersResult> ExportAsync(
            ExportChaptersRequest request,
            CancellationToken cancellationToken) =>
            ExportAsync(request, progress: null, cancellationToken);

        public async Task<ExportChaptersResult> ExportAsync(
            ExportChaptersRequest request,
            IProgress<ExportChaptersProgress>? progress,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            _progress = progress;
            Started.TrySetResult();
            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ObservedCancellation = true;
                    throw;
                }
            }

            return await _completion.Task.WaitAsync(cancellationToken);
        }

        public void Report(ExportChaptersProgress progress) => _progress?.Report(progress);

        public void Complete(ExportChaptersResult result) => _completion.TrySetResult(result);

        public void Fail(Exception exception) => _completion.TrySetException(exception);

        public void Reset()
        {
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
