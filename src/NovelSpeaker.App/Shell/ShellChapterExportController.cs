using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Shell;

/// <summary>
/// Projects the process-wide chapter export batch into shell UI and terminal feedback.
/// </summary>
public sealed partial class ShellChapterExportController : ObservableObject, IDisposable
{
    private readonly IChapterExportCoordinator _coordinator;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IPresentationLauncher _launcher;
    private readonly IUiScheduler _uiScheduler;
    private readonly OwnedTaskRegistry _processTasks = new();
    private Guid? _notifiedTerminalBatchId;
    private Guid? _dismissedBatchId;
    private bool _disposed;

    public ShellChapterExportController(
        IChapterExportCoordinator coordinator,
        IAppFeedbackService feedbackService,
        IPresentationLauncher launcher,
        IUiScheduler? uiScheduler = null)
    {
        _coordinator = coordinator;
        _feedbackService = feedbackService;
        _launcher = launcher;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        ApplySnapshot(coordinator.CurrentSnapshot, notifyTerminal: false);
        coordinator.SnapshotChanged += OnSnapshotChanged;
    }

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
    private string progressText = string.Empty;

    [ObservableProperty]
    private string currentChapterText = string.Empty;

    [ObservableProperty]
    private bool canCancel;

    [ObservableProperty]
    private bool canOpenDirectory;

    [ObservableProperty]
    private bool canDismiss;

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
                "取消章节导出失败",
                _feedbackService.Project(exception));
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenDirectory))]
    private async Task OpenDirectoryAsync(CancellationToken cancellationToken)
    {
        var snapshot = _coordinator.CurrentSnapshot;
        if (snapshot?.Status != ChapterExportBatchStatus.Completed ||
            string.IsNullOrWhiteSpace(snapshot.ExportDirectoryPath))
        {
            return;
        }

        try
        {
            await _launcher.OpenAsync(snapshot.ExportDirectoryPath, cancellationToken).ConfigureAwait(true);
            DismissSnapshot(snapshot.BatchId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification(
                "打开导出目录失败",
                _feedbackService.Project(exception));
        }
    }

    [RelayCommand(CanExecute = nameof(CanDismiss))]
    private void Dismiss()
    {
        var snapshot = _coordinator.CurrentSnapshot;
        if (snapshot is not null && snapshot.Status == ChapterExportBatchStatus.Completed)
        {
            DismissSnapshot(snapshot.BatchId);
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

    partial void OnCanCancelChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();

    partial void OnCanOpenDirectoryChanged(bool value) => OpenDirectoryCommand.NotifyCanExecuteChanged();

    partial void OnCanDismissChanged(bool value) => DismissCommand.NotifyCanExecuteChanged();

    private void OnSnapshotChanged(object? sender, ChapterExportSnapshot snapshot)
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

    private void ApplySnapshot(ChapterExportSnapshot? snapshot, bool notifyTerminal)
    {
        if (snapshot is null)
        {
            ClearProjection();
            return;
        }

        if (_dismissedBatchId == snapshot.BatchId && snapshot.Status == ChapterExportBatchStatus.Completed)
        {
            ClearProjection();
            return;
        }

        if (snapshot.Status == ChapterExportBatchStatus.Completed)
        {
            IsVisible = true;
            IsFlyoutOpen = false;
            BookTitle = snapshot.BookTitle;
            CompactStatusText = $"导出完成 · {snapshot.CompletedChapterCount} 章";
            BatchStatusText = "导出完成";
            ProgressText = BuildCompletionText(snapshot);
            CurrentChapterText = string.Empty;
            CanCancel = false;
            CanOpenDirectory = !string.IsNullOrWhiteSpace(snapshot.ExportDirectoryPath);
            CanDismiss = true;
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

        if (snapshot.Status is ChapterExportBatchStatus.Cancelled or ChapterExportBatchStatus.Failed)
        {
            if (notifyTerminal)
            {
                NotifyTerminalOnce(snapshot);
            }
            else
            {
                _notifiedTerminalBatchId = snapshot.BatchId;
            }

            ClearProjection();
            return;
        }

        _dismissedBatchId = null;
        IsVisible = true;
        BookTitle = snapshot.BookTitle;
        CompactStatusText = BuildCompactStatus(snapshot);
        BatchStatusText = snapshot.Status switch
        {
            ChapterExportBatchStatus.Waiting => "正在准备导出",
            ChapterExportBatchStatus.Cancelling => "正在取消导出",
            _ => "正在导出"
        };
        ProgressText = $"总进度 {snapshot.CompletedChapterCount} / {snapshot.TotalChapterCount} 章";
        CurrentChapterText = BuildCurrentChapterText(snapshot);
        CanCancel = snapshot.Status is ChapterExportBatchStatus.Waiting or ChapterExportBatchStatus.Running;
        CanOpenDirectory = false;
        CanDismiss = false;
    }

    private void DismissSnapshot(Guid batchId)
    {
        var snapshot = _coordinator.CurrentSnapshot;
        if (snapshot is null ||
            snapshot.BatchId != batchId ||
            snapshot.Status != ChapterExportBatchStatus.Completed)
        {
            return;
        }

        _dismissedBatchId = batchId;
        IsFlyoutOpen = false;
        ClearProjection();
    }

    private void ClearProjection()
    {
        IsVisible = false;
        IsFlyoutOpen = false;
        CompactStatusText = string.Empty;
        BookTitle = string.Empty;
        BatchStatusText = string.Empty;
        ProgressText = string.Empty;
        CurrentChapterText = string.Empty;
        CanCancel = false;
        CanOpenDirectory = false;
        CanDismiss = false;
    }

    private void NotifyTerminalOnce(ChapterExportSnapshot snapshot)
    {
        if (_notifiedTerminalBatchId == snapshot.BatchId)
        {
            return;
        }

        _notifiedTerminalBatchId = snapshot.BatchId;
        switch (snapshot.Status)
        {
            case ChapterExportBatchStatus.Completed:
                _feedbackService.ShowSuccess("导出完成", BuildCompletionText(snapshot));
                break;
            case ChapterExportBatchStatus.Cancelled:
                _feedbackService.ShowWarning("导出已取消", "已完成并发布的 MP3 文件会保留。");
                break;
            case ChapterExportBatchStatus.Failed:
                _feedbackService.ShowWarning(
                    "导出失败",
                    string.IsNullOrWhiteSpace(snapshot.ErrorSummary)
                        ? "章节导出失败，请重试。"
                        : snapshot.ErrorSummary.Trim());
                break;
        }
    }

    private void ReportProjectionFailure(Exception exception)
    {
        _feedbackService.ShowProjectedNotification(
            "更新章节导出状态失败",
            _feedbackService.Project(exception));
    }

    private static string BuildCompactStatus(ChapterExportSnapshot snapshot)
    {
        var percentage = Math.Clamp(
            (int)Math.Round(snapshot.Progress * 100d, MidpointRounding.AwayFromZero),
            0,
            100);
        return $"导出中 · {snapshot.CompletedChapterCount}/{snapshot.TotalChapterCount} 章 · {percentage}%";
    }

    private static string BuildCurrentChapterText(ChapterExportSnapshot snapshot)
    {
        if (snapshot.CurrentChapterIndex is null)
        {
            return "正在准备章节…";
        }

        var prefix = $"第 {snapshot.CurrentChapterIndex.Value + 1} 章";
        return string.IsNullOrWhiteSpace(snapshot.CurrentChapterTitle)
            ? prefix
            : $"{prefix} · {snapshot.CurrentChapterTitle}";
    }

    private static string BuildCompletionText(ChapterExportSnapshot snapshot) => snapshot.SkippedChapterCount == 0
        ? $"已导出 {snapshot.CompletedChapterCount} 章"
        : $"已导出 {snapshot.CompletedChapterCount} 章，跳过 {snapshot.SkippedChapterCount} 章";
}
