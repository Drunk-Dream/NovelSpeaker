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
        IsBusy = true;
        LastImportedPath = filePath;

        try
        {
            var analysis = await _bookImportService.AnalyzeAsync(filePath, null, cancellationToken);
            if (analysis.Status == BookImportAnalysisStatus.Failed)
            {
                PreviewText = analysis.PreviewText;
                IsEncodingPreviewVisible = analysis.FailureReason == BookImportFailureReason.UnsupportedEncoding;
                StatusMessage = analysis.FailureReason switch
                {
                    BookImportFailureReason.DuplicateBook => "这本书已经导入过了。",
                    BookImportFailureReason.NoValidChapters => "未识别到有效章节，请检查章节规则。",
                    BookImportFailureReason.UnsupportedEncoding => "自动识别编码失败，请切换编码后重试。",
                    _ => "导入失败，请重试。"
                };
                return;
            }

            var result = await _bookImportService.CommitAsync(analysis, cancellationToken);
            await LoadAsync(cancellationToken);
            StatusMessage = $"导入成功：{result.Title}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RetryWithEncodingAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(LastImportedPath))
        {
            return;
        }

        var analysis = await _bookImportService.AnalyzeAsync(LastImportedPath, SelectedEncoding, cancellationToken);
        if (analysis.Status == BookImportAnalysisStatus.ReadyToCommit)
        {
            var result = await _bookImportService.CommitAsync(analysis, cancellationToken);
            await LoadAsync(cancellationToken);
            StatusMessage = $"导入成功：{result.Title}";
            IsEncodingPreviewVisible = false;
        }
    }
}
