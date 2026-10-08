using NovelSpeaker.Application.Cache.Export;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Features.Cache;

internal sealed record CacheManagementExportChapter(
    int ChapterIndex,
    string Title,
    bool IsExportable);

internal sealed class CacheManagementExportController
{
    private readonly IChapterExportCoordinator _coordinator;
    private readonly IAppDialogService _dialogService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IPresentationFileDialogService _fileDialogs;
    private readonly IUiScheduler _uiScheduler;
    private CancellationTokenSource? _preparationCts;
    private PageActivationScope? _activation;

    public CacheManagementExportController(
        IChapterExportCoordinator coordinator,
        IAppDialogService dialogService,
        IAppFeedbackService feedbackService,
        IPresentationFileDialogService fileDialogs,
        IUiScheduler uiScheduler)
    {
        _coordinator = coordinator;
        _dialogService = dialogService;
        _feedbackService = feedbackService;
        _fileDialogs = fileDialogs;
        _uiScheduler = uiScheduler;
    }

    public event EventHandler? StateChanged;

    public bool IsBatchActive => _coordinator.CurrentSnapshot?.Status is
        ChapterExportBatchStatus.Waiting or
        ChapterExportBatchStatus.Running or
        ChapterExportBatchStatus.Cancelling;

    public void Activate(PageActivationScope activation)
    {
        Deactivate();
        _activation = activation;
        _coordinator.SnapshotChanged += OnSnapshotChanged;
        activation.Register(() => _coordinator.SnapshotChanged -= OnSnapshotChanged);
        activation.Register(() =>
        {
            if (ReferenceEquals(_activation, activation)) Deactivate();
        });
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Deactivate()
    {
        _activation = null;
        _preparationCts?.Cancel();
    }

    public async Task PrepareAndStartAsync(
        string bookId,
        string bookTitle,
        IReadOnlyList<CacheManagementExportChapter> selectedChapters,
        CancellationToken cancellationToken)
    {
        var exportableChapters = selectedChapters
            .Where(static chapter => chapter.IsExportable)
            .ToArray();
        var skippedChapterCount = selectedChapters.Count - exportableChapters.Length;
        if (exportableChapters.Length == 0)
        {
            _feedbackService.ShowWarning(
                "没有可导出的章节",
                "所选章节当前均不可导出，请先完成缓存后重试。");
            return;
        }

        var operationCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _activation?.CancellationToken ?? new CancellationToken(canceled: true));
        if (Interlocked.CompareExchange(ref _preparationCts, operationCts, null) is not null)
        {
            operationCts.Dispose();
            return;
        }

        try
        {
            if (skippedChapterCount > 0)
            {
                var decision = await _dialogService.ShowConfirmationAsync(
                    "跳过不可导出章节",
                    $"所选 {selectedChapters.Count} 章中有 {skippedChapterCount} 章当前不可导出。" +
                    $"是否跳过这 {skippedChapterCount} 章并导出其余 {exportableChapters.Length} 章？",
                    "跳过并导出",
                    "取消",
                    operationCts.Token);
                operationCts.Token.ThrowIfCancellationRequested();
                if (decision != AppConfirmationDecision.Confirm)
                {
                    return;
                }
            }

            var destinationRoot = await _fileDialogs.PickFolderAsync(
                new PresentationFolderDialogOptions("选择章节 MP3 导出位置"),
                operationCts.Token);
            operationCts.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(destinationRoot))
            {
                return;
            }

            var startResult = await _coordinator.StartAsync(
                new StartChapterExportRequest(
                    bookId,
                    bookTitle,
                    exportableChapters
                        .Select(static chapter => new ChapterExportSelection(
                            chapter.ChapterIndex,
                            chapter.Title))
                        .ToArray(),
                    destinationRoot,
                    skippedChapterCount),
                operationCts.Token);
            operationCts.Token.ThrowIfCancellationRequested();

            if (startResult.Status == ChapterExportStartStatus.BatchAlreadyActive)
            {
                _feedbackService.ShowWarning(
                    "已有导出任务",
                    startResult.Message ?? "已有章节导出任务正在运行。");
            }
            else if (startResult.Status != ChapterExportStartStatus.Accepted)
            {
                _feedbackService.ShowWarning(
                    "无法开始导出",
                    startResult.Message ?? "没有可导出的章节。");
            }
        }
        catch (OperationCanceledException) when (operationCts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!operationCts.IsCancellationRequested)
                _feedbackService.ShowProjectedNotification("开始导出失败", _feedbackService.Project(exception));
        }
        finally
        {
            Interlocked.CompareExchange(ref _preparationCts, null, operationCts);
            operationCts.Dispose();
        }
    }

    private void OnSnapshotChanged(object? sender, ChapterExportSnapshot snapshot)
    {
        if (_activation is not { IsCurrent: true } activation) return;
        activation.Run(token => _uiScheduler.InvokeAsync(
            () => activation.TryCommit(() => StateChanged?.Invoke(this, EventArgs.Empty)), token),
            exception => _feedbackService.ShowProjectedNotification(
                "更新导出状态失败", _feedbackService.Project(exception)));
    }
}
