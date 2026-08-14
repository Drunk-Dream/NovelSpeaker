namespace NovelSpeaker.Application.Playback.Export;

/// <summary>
/// Captures one export request and owns its execution, progress and cancellation for the process lifetime.
/// </summary>
public sealed class ChapterExportCoordinator : IChapterExportCoordinator, IAsyncDisposable
{
    private const string UnexpectedFailureSummary = "章节导出失败，请重试。";
    private static readonly TimeSpan DisposeWaitTimeout = TimeSpan.FromSeconds(5);
    private readonly IExportChaptersService _exportService;
    private readonly object _syncRoot = new();
    private ChapterExportSnapshot? _currentSnapshot;
    private CancellationTokenSource? _activeCancellation;
    private Task? _activeTask;
    private bool _isStarting;
    private bool _disposed;

    public ChapterExportCoordinator(IExportChaptersService exportService)
    {
        _exportService = exportService;
    }

    public ChapterExportSnapshot? CurrentSnapshot => Volatile.Read(ref _currentSnapshot);

    public event EventHandler<ChapterExportSnapshot>? SnapshotChanged;

    public Task<ChapterExportStartResult> StartAsync(
        StartChapterExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BookId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BookTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationRootDirectory);
        ArgumentNullException.ThrowIfNull(request.Chapters);
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            if (_isStarting || _activeTask is { IsCompleted: false })
            {
                return Task.FromResult(new ChapterExportStartResult(
                    ChapterExportStartStatus.BatchAlreadyActive,
                    null,
                    "已有章节导出任务正在运行。"));
            }

            _isStarting = true;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chapters = request.Chapters
                .DistinctBy(chapter => chapter.ChapterIndex)
                .OrderBy(chapter => chapter.ChapterIndex)
                .ToArray();
            if (chapters.Length == 0)
            {
                return Task.FromResult(new ChapterExportStartResult(
                    ChapterExportStartStatus.NoChaptersSelected,
                    null,
                    "请至少选择一个可导出的章节。"));
            }

            if (chapters[0].ChapterIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request));
            }

            var batchId = Guid.NewGuid();
            var frozen = new FrozenBatch(
                batchId,
                request.BookId,
                request.BookTitle.Trim(),
                chapters,
                request.DestinationRootDirectory,
                Math.Max(0, request.SkippedChapterCount));
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var batchCancellation = new CancellationTokenSource();

            lock (_syncRoot)
            {
                ThrowIfDisposed();
                _activeCancellation?.Dispose();
                _activeCancellation = batchCancellation;
                _activeTask = completion.Task;
                _isStarting = false;
            }

            Publish(new ChapterExportSnapshot(
                batchId,
                frozen.BookId,
                frozen.BookTitle,
                ChapterExportBatchStatus.Waiting,
                frozen.Chapters.Count,
                0,
                frozen.SkippedChapterCount,
                null,
                null,
                frozen.DestinationRootDirectory,
                null,
                null));
            _ = RunOwnedBatchAsync(frozen, batchCancellation, completion);
            return Task.FromResult(new ChapterExportStartResult(
                ChapterExportStartStatus.Accepted,
                batchId,
                null));
        }
        finally
        {
            lock (_syncRoot)
            {
                _isStarting = false;
            }
        }
    }

    public async Task CancelAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Task? activeTask;
        lock (_syncRoot)
        {
            activeTask = _activeTask;
            var activeCancellation = _activeCancellation;
            if (activeTask is null || activeTask.IsCompleted || activeCancellation is null)
            {
                return;
            }

            var snapshot = CurrentSnapshot;
            if (snapshot is not null &&
                snapshot.Status is ChapterExportBatchStatus.Waiting or ChapterExportBatchStatus.Running)
            {
                Publish(snapshot with { Status = ChapterExportBatchStatus.Cancelling });
            }

            // Keep the cancellation and active-slot check under the same lock. A
            // replacement batch may otherwise dispose this CTS between the two.
            activeCancellation.Cancel();
        }

        await activeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WaitForCurrentBatchAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Task? activeTask;
        lock (_syncRoot)
        {
            activeTask = _activeTask;
        }

        if (activeTask is not null)
        {
            await activeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? activeTask;
        CancellationTokenSource? activeCancellation;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            activeTask = _activeTask;
            activeCancellation = _activeCancellation;
            activeCancellation?.Cancel();
        }

        var completed = true;
        if (activeTask is not null && !activeTask.IsCompleted)
        {
            try
            {
                await activeTask
                    .WaitAsync(DisposeWaitTimeout)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                completed = false;
            }
        }

        if (completed)
        {
            activeCancellation?.Dispose();
        }
    }

    private async Task RunOwnedBatchAsync(
        FrozenBatch batch,
        CancellationTokenSource batchCancellation,
        TaskCompletionSource completion)
    {
        try
        {
            Publish(CurrentSnapshot! with { Status = ChapterExportBatchStatus.Running });
            var progress = new InlineProgress<ExportChaptersProgress>(value => ApplyProgress(batch, value));
            var result = await _exportService
                .ExportAsync(
                    new ExportChaptersRequest(
                        batch.BookId,
                        batch.Chapters.Select(chapter => chapter.ChapterIndex).ToArray(),
                        batch.DestinationRootDirectory,
                        batch.BookTitle,
                        batch.Chapters.ToDictionary(
                            chapter => chapter.ChapterIndex,
                            chapter => chapter.ChapterTitle)),
                    progress,
                    batchCancellation.Token)
                .ConfigureAwait(false);
            batchCancellation.Token.ThrowIfCancellationRequested();

            if (result.Status == ExportChaptersStatus.Succeeded &&
                result.Files.Count == batch.Chapters.Count)
            {
                Publish(CurrentSnapshot! with
                {
                    Status = ChapterExportBatchStatus.Completed,
                    CompletedChapterCount = batch.Chapters.Count,
                    CurrentChapterIndex = null,
                    CurrentChapterTitle = null,
                    ExportDirectoryPath = result.ExportDirectoryPath,
                    ErrorSummary = null
                });
            }
            else
            {
                Publish(CurrentSnapshot! with
                {
                    Status = ChapterExportBatchStatus.Failed,
                    CurrentChapterIndex = result.FailedChapterIndex,
                    CurrentChapterTitle = ResolveChapterTitle(batch, result.FailedChapterIndex),
                    ErrorSummary = ProjectFailure(result)
                });
            }
        }
        catch (OperationCanceledException) when (batchCancellation.IsCancellationRequested)
        {
            var snapshot = CurrentSnapshot;
            if (snapshot is not null)
            {
                Publish(snapshot with
                {
                    Status = ChapterExportBatchStatus.Cancelled,
                    CurrentChapterIndex = null,
                    CurrentChapterTitle = null,
                    ErrorSummary = null
                });
            }
        }
        catch (Exception)
        {
            var snapshot = CurrentSnapshot;
            if (snapshot is not null)
            {
                Publish(snapshot with
                {
                    Status = ChapterExportBatchStatus.Failed,
                    ErrorSummary = UnexpectedFailureSummary
                });
            }
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private void ApplyProgress(FrozenBatch batch, ExportChaptersProgress progress)
    {
        var snapshot = CurrentSnapshot;
        if (snapshot is null || snapshot.BatchId != batch.BatchId)
        {
            return;
        }

        int? currentChapterIndex = batch.Chapters.Any(
            chapter => chapter.ChapterIndex == progress.CurrentChapterIndex)
            ? progress.CurrentChapterIndex
            : null;
        Publish(snapshot with
        {
            Status = snapshot.Status == ChapterExportBatchStatus.Cancelling
                ? ChapterExportBatchStatus.Cancelling
                : ChapterExportBatchStatus.Running,
            CompletedChapterCount = Math.Max(
                snapshot.CompletedChapterCount,
                Math.Clamp(progress.CompletedChapterCount, 0, snapshot.TotalChapterCount)),
            CurrentChapterIndex = currentChapterIndex,
            CurrentChapterTitle = ResolveChapterTitle(batch, currentChapterIndex)
        });
    }

    private void Publish(ChapterExportSnapshot snapshot)
    {
        Volatile.Write(ref _currentSnapshot, snapshot);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private static string? ResolveChapterTitle(FrozenBatch batch, int? chapterIndex)
    {
        if (chapterIndex is null)
        {
            return null;
        }

        return batch.Chapters
            .FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex.Value)
            ?.ChapterTitle;
    }

    private static string ProjectFailure(ExportChaptersResult result) => result.Status switch
    {
        ExportChaptersStatus.IncompleteCache => "所选章节缓存已发生变化，请刷新后重试。",
        ExportChaptersStatus.SelectedRuleUnavailable => "当前 TTS 规则不可用，请在播放页选择已启用规则后重试。",
        ExportChaptersStatus.ChapterHasNoPlayableSegments => FormatChapterFailure(result.FailedChapterIndex, "没有可播放段落"),
        ExportChaptersStatus.BookNotFound or ExportChaptersStatus.ChapterNotFound => "书籍或章节已发生变化，请重新选择后重试。",
        ExportChaptersStatus.ChapterSpeechPlanUnavailable => FormatChapterFailure(result.FailedChapterIndex, "章节朗读清单尚未就绪"),
        _ => UnexpectedFailureSummary
    };

    private static string FormatChapterFailure(int? chapterIndex, string reason) => chapterIndex is null
        ? $"所选章节{reason}。"
        : $"第 {chapterIndex.Value + 1} 章{reason}。";

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record FrozenBatch(
        Guid BatchId,
        string BookId,
        string BookTitle,
        IReadOnlyList<ChapterExportSelection> Chapters,
        string DestinationRootDirectory,
        int SkippedChapterCount);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
