using System.Collections.Concurrent;
using NovelSpeaker.Application.Playback.Export;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class ChapterExportCoordinatorTests
{
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
        private readonly TaskCompletionSource<ExportChaptersResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IProgress<ExportChaptersProgress>? _progress;

        public bool WaitForCancellation { get; init; }

        public bool ObservedCancellation { get; private set; }

        public ExportChaptersRequest? LastRequest { get; private set; }

        public TaskCompletionSource Started { get; } =
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
    }
}
