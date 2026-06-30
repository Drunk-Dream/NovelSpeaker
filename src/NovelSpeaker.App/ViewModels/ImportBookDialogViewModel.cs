using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Dialogs;
using NovelSpeaker.App.Feedback;
using System.IO;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class ImportBookDialogViewModel : ObservableObject
{
    private readonly IBookImportService _bookImportService;
    private readonly IAppFeedbackService _feedbackService;
    private CancellationTokenSource? _activeOperationCancellationTokenSource;
    private BookImportAnalysis? _pendingAnalysis;
    private string? _filePath;
    private int _operationVersion;
    private bool _isDismissed;

    public ImportBookDialogViewModel(
        IBookImportService bookImportService,
        IAppFeedbackService feedbackService)
    {
        _bookImportService = bookImportService;
        _feedbackService = feedbackService;
    }

    public event Action<ImportBookDialogOutcome>? CloseRequested;

    [ObservableProperty]
    private string dialogTitle = "导入 TXT 小说";

    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private string fileSizeText = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string previewText = string.Empty;

    [ObservableProperty]
    private string selectedEncoding = "utf-8";

    [ObservableProperty]
    private string detectedEncodingText = "待识别";

    [ObservableProperty]
    private bool canConfirmImport;

    [ObservableProperty]
    private bool isProgressIndeterminate;

    [ObservableProperty]
    private double importProgressPercent;

    [ObservableProperty]
    private string importProgressText = string.Empty;

    [ObservableProperty]
    private string suggestedTitle = string.Empty;

    [ObservableProperty]
    private string? suggestedAuthor;

    [ObservableProperty]
    private bool isFileNameTemplateMatched;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isEncodingPreviewVisible;

    public async Task InitializeAsync(string filePath, CancellationToken cancellationToken)
    {
        _filePath = filePath;
        FileName = Path.GetFileName(filePath);
        FileSizeText = FormatFileSize(filePath);
        await AnalyzeAsync(new BookImportRequest(filePath, null), cancellationToken);
    }

    [RelayCommand]
    private async Task RetryPreviewAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        await AnalyzeAsync(new BookImportRequest(_filePath, SelectedEncoding), cancellationToken);
    }

    [RelayCommand]
    private async Task ConfirmImportAsync(CancellationToken cancellationToken)
    {
        if (_pendingAnalysis is null)
        {
            return;
        }

        await RunImportOperationAsync(
            async (context, progress) =>
            {
                await _bookImportService.CommitAsync(_pendingAnalysis, progress, context.CancellationToken);
                if (!context.IsCurrent())
                {
                    return;
                }

                CloseRequested?.Invoke(ImportBookDialogOutcome.Imported);
            },
            cancellationToken);
    }

    [RelayCommand]
    private void CancelDialog()
    {
        Dismiss();
        CloseRequested?.Invoke(ImportBookDialogOutcome.Cancelled);
    }

    public void Dismiss()
    {
        _isDismissed = true;
        CancelActiveOperation();
    }

    private async Task AnalyzeAsync(BookImportRequest request, CancellationToken cancellationToken)
    {
        ResetPendingAnalysis();
        await RunImportOperationAsync(
            async (context, progress) =>
            {
                var analysis = await _bookImportService.AnalyzeAsync(request, progress, context.CancellationToken);
                if (!context.IsCurrent())
                {
                    return;
                }

                ApplyAnalysis(analysis);
            },
            cancellationToken);
    }

    private async Task RunImportOperationAsync(
        Func<ImportOperationContext, IProgress<BookImportProgress>, Task> operation,
        CancellationToken cancellationToken)
    {
        var version = Interlocked.Increment(ref _operationVersion);
        ReplaceActiveOperation(cancellationToken);
        var activeCts = _activeOperationCancellationTokenSource!;
        var progress = new CallbackProgress<BookImportProgress>(UpdateProgress);
        var context = new ImportOperationContext(
            activeCts.Token,
            () => IsActiveOperation(version, activeCts));
        SetBusyState(version, true);

        try
        {
            await operation(context, progress);
        }
        catch (OperationCanceledException) when (activeCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsActiveOperation(version, activeCts))
            {
                return;
            }

            var projected = _feedbackService.Project(exception);
            if (!projected.IsSilent)
            {
                StatusMessage = projected.UserMessage;
            }
        }
        finally
        {
            if (ReferenceEquals(_activeOperationCancellationTokenSource, activeCts))
            {
                _activeOperationCancellationTokenSource = null;
            }

            activeCts.Dispose();
            SetBusyState(version, false);
        }
    }

    private void ApplyAnalysis(BookImportAnalysis analysis)
    {
        PreviewText = analysis.PreviewText;
        SuggestedTitle = analysis.SuggestedTitle;
        SuggestedAuthor = analysis.SuggestedAuthor;
        IsFileNameTemplateMatched = analysis.IsFileNameTemplateMatched;
        IsEncodingPreviewVisible = !string.IsNullOrWhiteSpace(analysis.PreviewText) ||
            analysis.FailureReason == BookImportFailureReason.UnsupportedEncoding;

        if (!string.Equals(analysis.DetectedEncoding, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            SelectedEncoding = analysis.DetectedEncoding;
            DetectedEncodingText = analysis.DetectedEncoding;
        }
        else
        {
            DetectedEncodingText = "未知";
        }

        if (analysis.Status == BookImportAnalysisStatus.ReadyToCommit)
        {
            _pendingAnalysis = analysis;
            CanConfirmImport = true;
            StatusMessage = string.Empty;
            ImportProgressText = "预览已准备好，可以确认导入。";
            return;
        }

        _pendingAnalysis = null;
        CanConfirmImport = false;
        StatusMessage = analysis.FailureReason switch
        {
            BookImportFailureReason.DuplicateBook => "这本书已经在书库中了。",
            BookImportFailureReason.NoValidChapters => "小说内容为空或无法识别为可导入文本。",
            BookImportFailureReason.UnsupportedEncoding => "自动识别编码失败，请切换编码并重新预览。",
            BookImportFailureReason.FileReadFailed => "文件无法读取，请确认文件仍可访问。",
            _ => "导入失败，请重试。"
        };
    }

    private void ResetPendingAnalysis()
    {
        _pendingAnalysis = null;
        CanConfirmImport = false;
        PreviewText = string.Empty;
        SuggestedTitle = string.Empty;
        SuggestedAuthor = null;
        IsFileNameTemplateMatched = false;
        IsEncodingPreviewVisible = false;
        ImportProgressPercent = 0;
        ImportProgressText = string.Empty;
        StatusMessage = string.Empty;
    }

    private void ReplaceActiveOperation(CancellationToken cancellationToken)
    {
        CancelActiveOperation();
        _activeOperationCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    private void CancelActiveOperation()
    {
        _activeOperationCancellationTokenSource?.Cancel();
        _activeOperationCancellationTokenSource?.Dispose();
        _activeOperationCancellationTokenSource = null;
    }

    private bool IsActiveOperation(int version, CancellationTokenSource activeCts)
    {
        return !_isDismissed &&
            version == Volatile.Read(ref _operationVersion) &&
            ReferenceEquals(_activeOperationCancellationTokenSource, activeCts) &&
            !activeCts.IsCancellationRequested;
    }

    private void SetBusyState(int version, bool isBusy)
    {
        if (version != Volatile.Read(ref _operationVersion))
        {
            return;
        }

        IsBusy = isBusy;
        if (!isBusy)
        {
            IsProgressIndeterminate = false;
        }
    }

    private void UpdateProgress(BookImportProgress progress)
    {
        ImportProgressText = progress.Message;
        IsProgressIndeterminate = progress.IsIndeterminate || progress.TotalBytes <= 0;
        ImportProgressPercent = IsProgressIndeterminate
            ? 0
            : Math.Round(progress.BytesProcessed * 100d / progress.TotalBytes, 1);
    }

    private static string FormatFileSize(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            return "文件不可用";
        }

        const double kilobyte = 1024d;
        const double megabyte = kilobyte * 1024d;
        var size = fileInfo.Length;
        return size >= megabyte
            ? $"{size / megabyte:0.0} MB"
            : $"{Math.Max(1d, size / kilobyte):0.0} KB";
    }

    private sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public CallbackProgress(Action<T> callback)
        {
            _callback = callback;
        }

        public void Report(T value)
        {
            _callback(value);
        }
    }

    private sealed record ImportOperationContext(
        CancellationToken CancellationToken,
        Func<bool> IsCurrent);
}
