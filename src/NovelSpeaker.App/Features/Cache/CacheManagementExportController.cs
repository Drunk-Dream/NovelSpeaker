using NovelSpeaker.Application.Cache.Export;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
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
    private readonly OwnedTaskRegistry _pageTasks = new();
    private CancellationTokenSource? _preparationCts;
    private CancellationToken _activationToken;
    private int _activationVersion;
    private bool _isActive;

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

    public void Activate(CancellationToken cancellationToken)
    {
        Deactivate();
        _activationToken = cancellationToken;
        _activationVersion++;
        _isActive = true;
        _coordinator.SnapshotChanged += OnSnapshotChanged;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Deactivate()
    {
        _activationVersion++;
        if (_isActive)
        {
            _coordinator.SnapshotChanged -= OnSnapshotChanged;
            _isActive = false;
        }

        _preparationCts?.Cancel();
        _activationToken = default;
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
            _activationToken);
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
        if (!_isActive || _activationToken.IsCancellationRequested)
        {
            return;
        }

        var activationVersion = _activationVersion;
        var activationToken = _activationToken;
        if (!_uiScheduler.CheckAccess())
        {
            try
            {
                _pageTasks.Register(
                    _uiScheduler.InvokeAsync(
                        () => NotifyStateChanged(activationVersion, activationToken),
                        activationToken),
                    exception => ReportProjectionFailure(exception, activationVersion, activationToken));
            }
            catch (OperationCanceledException) when (activationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ReportProjectionFailure(exception, activationVersion, activationToken);
            }

            return;
        }

        NotifyStateChanged(activationVersion, activationToken);
    }

    private void NotifyStateChanged(int activationVersion, CancellationToken activationToken)
    {
        if (!IsCurrentActivation(activationVersion, activationToken))
        {
            return;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReportProjectionFailure(
        Exception exception,
        int activationVersion,
        CancellationToken activationToken)
    {
        if (IsCurrentActivation(activationVersion, activationToken))
        {
            _feedbackService.ShowProjectedNotification(
                "更新导出状态失败",
                _feedbackService.Project(exception));
        }
    }

    private bool IsCurrentActivation(int activationVersion, CancellationToken activationToken) =>
        _isActive &&
        activationVersion == _activationVersion &&
        _activationToken == activationToken &&
        !activationToken.IsCancellationRequested;
}
