using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Drives the library page import experience and displays imported books.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly IBookImportService _bookImportService;
    private readonly IBookCatalogService _bookCatalogService;
    private BookImportAnalysis? _pendingAnalysis;
    private CancellationTokenSource? _activeImportCancellationTokenSource;

    public LibraryViewModel(
        IBookImportService bookImportService,
        IBookCatalogService bookCatalogService)
    {
        _bookImportService = bookImportService;
        _bookCatalogService = bookCatalogService;
    }

    public ObservableCollection<LibraryBookItemViewModel> Books { get; } = [];

    [ObservableProperty]
    private string statusMessage = "导入一本 TXT，开始建立你的书库。";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isEncodingPreviewVisible;

    [ObservableProperty]
    private string previewText = string.Empty;

    [ObservableProperty]
    private string selectedEncoding = "utf-8";

    [ObservableProperty]
    private bool canConfirmImport;

    [ObservableProperty]
    private bool isProgressIndeterminate;

    [ObservableProperty]
    private double importProgressPercent;

    [ObservableProperty]
    private string importProgressText = string.Empty;

    public string? LastImportedPath { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var books = await _bookCatalogService.GetBooksAsync(cancellationToken);
        Books.Clear();

        foreach (var book in books)
        {
            Books.Add(new LibraryBookItemViewModel(
                book.Id,
                book.Title,
                book.Author,
                book.CurrentChapterTitle,
                book.ImportedAt));
        }
    }

    public async Task ImportFileAsync(string filePath, CancellationToken cancellationToken)
    {
        LastImportedPath = filePath;
        await AnalyzeAsync(new BookImportRequest(filePath, null), cancellationToken);
    }

    [RelayCommand]
    private async Task RetryWithEncodingAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(LastImportedPath))
        {
            return;
        }

        await AnalyzeAsync(new BookImportRequest(LastImportedPath, SelectedEncoding), cancellationToken);
    }

    [RelayCommand]
    private async Task ConfirmImportAsync(CancellationToken cancellationToken)
    {
        if (_pendingAnalysis is null)
        {
            return;
        }

        await RunImportOperationAsync(
            async (linkedToken, progress) =>
            {
                var result = await _bookImportService.CommitAsync(_pendingAnalysis, progress, linkedToken);
                await LoadAsync(linkedToken);
                ClearPendingAnalysis();
                StatusMessage = $"导入成功：{result.Title}";
            },
            cancellationToken);
    }

    [RelayCommand]
    private void CancelImport()
    {
        _activeImportCancellationTokenSource?.Cancel();
    }

    private async Task AnalyzeAsync(BookImportRequest request, CancellationToken cancellationToken)
    {
        ClearPendingAnalysis();
        await RunImportOperationAsync(
            async (linkedToken, progress) =>
            {
                var analysis = await _bookImportService.AnalyzeAsync(request, progress, linkedToken);
                ApplyAnalysis(analysis);
            },
            cancellationToken);
    }

    private async Task RunImportOperationAsync(
        Func<CancellationToken, IProgress<BookImportProgress>, Task> operation,
        CancellationToken cancellationToken)
    {
        _activeImportCancellationTokenSource?.Cancel();
        _activeImportCancellationTokenSource?.Dispose();
        _activeImportCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var progress = new CallbackProgress<BookImportProgress>(UpdateProgress);
        IsBusy = true;

        try
        {
            await operation(_activeImportCancellationTokenSource.Token, progress);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "导入已取消。";
            ImportProgressText = "已取消当前导入任务。";
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            _activeImportCancellationTokenSource.Dispose();
            _activeImportCancellationTokenSource = null;
        }
    }

    private void ApplyAnalysis(BookImportAnalysis analysis)
    {
        PreviewText = analysis.PreviewText;
        IsEncodingPreviewVisible = !string.IsNullOrWhiteSpace(analysis.PreviewText) ||
            analysis.FailureReason == BookImportFailureReason.UnsupportedEncoding;

        if (!string.Equals(analysis.DetectedEncoding, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            SelectedEncoding = analysis.DetectedEncoding;
        }

        if (analysis.Status == BookImportAnalysisStatus.ReadyToCommit)
        {
            _pendingAnalysis = analysis;
            CanConfirmImport = true;
            StatusMessage = "已完成导入分析，请确认导入。";
            ImportProgressText = "预览已准备好，可以确认导入。";
            return;
        }

        _pendingAnalysis = null;
        CanConfirmImport = false;
        StatusMessage = analysis.FailureReason switch
        {
            BookImportFailureReason.DuplicateBook => "这本书已经导入过了。",
            BookImportFailureReason.NoValidChapters => "小说内容为空或无法识别为可导入文本。",
            BookImportFailureReason.UnsupportedEncoding => "自动识别编码失败，请切换编码并重新预览。",
            _ => "导入失败，请重试。"
        };
    }

    private void ClearPendingAnalysis()
    {
        _pendingAnalysis = null;
        CanConfirmImport = false;
        IsEncodingPreviewVisible = false;
        PreviewText = string.Empty;
        ImportProgressPercent = 0;
        ImportProgressText = string.Empty;
    }

    private void UpdateProgress(BookImportProgress progress)
    {
        ImportProgressText = progress.Message;
        IsProgressIndeterminate = progress.IsIndeterminate || progress.TotalBytes <= 0;
        ImportProgressPercent = IsProgressIndeterminate
            ? 0
            : Math.Round(progress.BytesProcessed * 100d / progress.TotalBytes, 1);
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
}
