using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Cache.ActiveCache;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Shell;

/// <summary>
/// Projects the process-owned active-cache snapshot for the shell without owning
/// or reproducing the background batch state machine.
/// </summary>
public sealed partial class ShellActiveCacheController : ObservableObject, IDisposable
{
    private const string SafeFailureMessage = "主动缓存失败，请重试。";
    private readonly IActiveCacheCoordinator _coordinator;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IUiScheduler _uiScheduler;
    private readonly OwnedTaskRegistry _processTasks = new();
    private readonly Dictionary<int, int> _chapterPositions = [];
    private readonly Dictionary<int, ShellActiveCacheChapterItem> _chapterRows = [];
    private Guid? _projectedBatchId;
    private Guid? _notifiedTerminalBatchId;
    private bool _disposed;

    public ShellActiveCacheController(
        IActiveCacheCoordinator coordinator,
        IAppFeedbackService feedbackService,
        IUiScheduler? uiScheduler = null)
    {
        _coordinator = coordinator;
        _feedbackService = feedbackService;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        ApplySnapshot(coordinator.CurrentSnapshot, notifyTerminal: false);
        coordinator.SnapshotChanged += OnSnapshotChanged;
    }

    public ObservableCollection<ShellActiveCacheChapterItem> Chapters { get; } = [];

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private bool isFlyoutOpen;

    [ObservableProperty]
    private string compactStatusText = string.Empty;

    [ObservableProperty]
    private string bookTitle = string.Empty;

    [ObservableProperty]
    private string batchStatusText = string.Empty;

    [ObservableProperty]
    private string totalSegmentProgressText = string.Empty;

    [ObservableProperty]
    private bool canCancel;

    [RelayCommand]
    private void ToggleFlyout()
    {
        if (IsVisible)
        {
            IsFlyoutOpen = !IsFlyoutOpen;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _coordinator.CancelAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification(
                "取消主动缓存失败",
                _feedbackService.Project(exception));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.SnapshotChanged -= OnSnapshotChanged;
    }

    partial void OnCanCancelChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void OnSnapshotChanged(object? sender, ActiveCacheSnapshot snapshot)
    {
        if (!_uiScheduler.CheckAccess())
        {
            _processTasks.Register(
                _uiScheduler.InvokeAsync(() => ApplySnapshot(snapshot, notifyTerminal: true)),
                ReportProjectionFailure);
            return;
        }

        ApplySnapshot(snapshot, notifyTerminal: true);
    }

    private void ApplySnapshot(ActiveCacheSnapshot? snapshot, bool notifyTerminal)
    {
        if (snapshot is null)
        {
            ClearActiveProjection();
            return;
        }

        var isActive = IsActive(snapshot.Status);
        IsVisible = isActive;
        if (!isActive)
        {
            IsFlyoutOpen = false;
            CanCancel = false;
            if (notifyTerminal)
            {
                NotifyTerminalOnce(snapshot);
            }
            else
            {
                _notifiedTerminalBatchId = snapshot.BatchId;
            }

            return;
        }

        BookTitle = snapshot.BookTitle;
        CompactStatusText = BuildCompactStatus(snapshot);
        BatchStatusText = snapshot.Status switch
        {
            ActiveCacheBatchStatus.Waiting => "正在等待",
            ActiveCacheBatchStatus.Cancelling => "正在取消",
            _ => "正在缓存"
        };
        TotalSegmentProgressText =
            $"总进度 {snapshot.CompletedSegmentCount} / {snapshot.TotalSegmentCount} 段";
        CanCancel = snapshot.Status is ActiveCacheBatchStatus.Waiting or ActiveCacheBatchStatus.Running;

        ProjectChapters(snapshot);
    }

    private void ClearActiveProjection()
    {
        IsVisible = false;
        IsFlyoutOpen = false;
        CompactStatusText = string.Empty;
        BookTitle = string.Empty;
        BatchStatusText = string.Empty;
        TotalSegmentProgressText = string.Empty;
        CanCancel = false;
        Chapters.Clear();
        _chapterPositions.Clear();
        _chapterRows.Clear();
        _projectedBatchId = null;
    }

    private void ProjectChapters(ActiveCacheSnapshot snapshot)
    {
        if (_projectedBatchId != snapshot.BatchId || snapshot.Chapters.Count != Chapters.Count)
        {
            RebuildChapterRows(snapshot);
            return;
        }

        if (snapshot.CurrentChapterIndex is not int currentChapterIndex)
        {
            return;
        }

        if (!_chapterPositions.TryGetValue(currentChapterIndex, out var chapterPosition))
        {
            RebuildChapterRows(snapshot);
            return;
        }

        var currentChapter = snapshot.Chapters[chapterPosition];
        if (currentChapter.ChapterIndex != currentChapterIndex)
        {
            // The coordinator freezes the selected chapters for a batch. Rebuild defensively
            // if that contract ever changes without a new batch identity.
            RebuildChapterRows(snapshot);
            return;
        }

        _chapterRows[currentChapterIndex].Update(
            currentChapter.ChapterTitle,
            BuildChapterStatus(currentChapter),
            currentChapter.Status == ActiveCacheChapterStatus.Running,
            currentChapter.Status is ActiveCacheChapterStatus.Completed or ActiveCacheChapterStatus.Skipped,
            currentChapter.Status == ActiveCacheChapterStatus.Failed);
    }

    private void RebuildChapterRows(ActiveCacheSnapshot snapshot)
    {
        Chapters.Clear();
        _chapterPositions.Clear();
        _chapterRows.Clear();
        for (var position = 0; position < snapshot.Chapters.Count; position++)
        {
            var chapter = snapshot.Chapters[position];
            var row = CreateChapterRow(chapter);
            Chapters.Add(row);
            _chapterPositions.Add(chapter.ChapterIndex, position);
            _chapterRows.Add(chapter.ChapterIndex, row);
        }

        _projectedBatchId = snapshot.BatchId;
    }

    private static ShellActiveCacheChapterItem CreateChapterRow(ActiveCacheChapterSnapshot chapter) =>
        new(
            chapter.ChapterIndex,
            chapter.ChapterTitle,
            BuildChapterStatus(chapter),
            chapter.Status == ActiveCacheChapterStatus.Running,
            chapter.Status is ActiveCacheChapterStatus.Completed or ActiveCacheChapterStatus.Skipped,
            chapter.Status == ActiveCacheChapterStatus.Failed);

    private void NotifyTerminalOnce(ActiveCacheSnapshot snapshot)
    {
        if (_notifiedTerminalBatchId == snapshot.BatchId)
        {
            return;
        }

        _notifiedTerminalBatchId = snapshot.BatchId;
        switch (snapshot.Status)
        {
            case ActiveCacheBatchStatus.Completed:
                _feedbackService.ShowSuccess(
                    "主动缓存完成",
                    BuildBatchSummary(snapshot));
                break;
            case ActiveCacheBatchStatus.Cancelled:
                _feedbackService.ShowWarning("主动缓存已取消", "已完成的缓存会保留。");
                break;
            case ActiveCacheBatchStatus.Failed:
                _feedbackService.ShowWarning(
                    "主动缓存失败",
                    BuildBatchSummary(snapshot) + (string.IsNullOrWhiteSpace(snapshot.ErrorSummary)
                        ? SafeFailureMessage
                        : snapshot.ErrorSummary.Trim()));
                break;
        }
    }

    private static string BuildBatchSummary(ActiveCacheSnapshot snapshot) =>
        $"缓存完成：成功 {snapshot.Chapters.Count(chapter => chapter.Status == ActiveCacheChapterStatus.Completed)}，跳过 {snapshot.Chapters.Count(chapter => chapter.Status == ActiveCacheChapterStatus.Skipped)}，失败 {snapshot.Chapters.Count(chapter => chapter.Status == ActiveCacheChapterStatus.Failed)}。";

    private void ReportProjectionFailure(Exception exception)
    {
        _feedbackService.ShowProjectedNotification(
            "更新主动缓存状态失败",
            _feedbackService.Project(exception));
    }

    private static bool IsActive(ActiveCacheBatchStatus status) =>
        status is
            ActiveCacheBatchStatus.Waiting or
            ActiveCacheBatchStatus.Running or
            ActiveCacheBatchStatus.Cancelling;

    private static string BuildCompactStatus(ActiveCacheSnapshot snapshot)
    {
        var percentage = Math.Clamp(
            (int)Math.Round(snapshot.Progress * 100d, MidpointRounding.AwayFromZero),
            0,
            100);
        return $"缓存中 · {snapshot.CompletedChapterCount}/{snapshot.TotalChapterCount} 章 · {percentage}%";
    }

    private static string BuildChapterStatus(ActiveCacheChapterSnapshot chapter) =>
        chapter.Status switch
        {
            ActiveCacheChapterStatus.Completed => "已完成",
            ActiveCacheChapterStatus.Skipped => "已缓存，跳过",
            ActiveCacheChapterStatus.Running =>
                $"{chapter.CompletedSegmentCount} / {chapter.TotalSegmentCount}",
            ActiveCacheChapterStatus.Cancelled => "已取消",
            ActiveCacheChapterStatus.Failed => "失败",
            _ => "等待中"
        };
}
