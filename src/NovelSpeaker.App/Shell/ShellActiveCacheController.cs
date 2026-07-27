using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback.ActiveCache;
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

        Chapters.Clear();
        foreach (var chapter in snapshot.Chapters)
        {
            Chapters.Add(new ShellActiveCacheChapterItem(
                chapter.ChapterIndex,
                chapter.ChapterTitle,
                BuildChapterStatus(chapter),
                chapter.Status == ActiveCacheChapterStatus.Running,
                chapter.Status == ActiveCacheChapterStatus.Completed,
                chapter.Status == ActiveCacheChapterStatus.Failed));
        }
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
    }

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
                    $"已缓存 {snapshot.CompletedChapterCount} 章。");
                break;
            case ActiveCacheBatchStatus.Cancelled:
                _feedbackService.ShowWarning("主动缓存已取消", "已完成的缓存会保留。");
                break;
            case ActiveCacheBatchStatus.Failed:
                _feedbackService.ShowWarning(
                    "主动缓存失败",
                    string.IsNullOrWhiteSpace(snapshot.ErrorSummary)
                        ? SafeFailureMessage
                        : snapshot.ErrorSummary.Trim());
                break;
        }
    }

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
            ActiveCacheChapterStatus.Running =>
                $"{chapter.CompletedSegmentCount} / {chapter.TotalSegmentCount}",
            ActiveCacheChapterStatus.Cancelled => "已取消",
            ActiveCacheChapterStatus.Failed => "失败",
            _ => "等待中"
        };
}
