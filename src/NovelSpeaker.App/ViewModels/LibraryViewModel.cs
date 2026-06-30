using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Library;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Drives the library page import experience and displays imported books.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private static readonly IReadOnlyList<LibrarySortOption> SortOptions =
    [
        new(LibrarySortMode.RecentReading, "最近阅读"),
        new(LibrarySortMode.Title, "书名")
    ];

    private readonly IBookImportService _bookImportService;
    private readonly IBookCatalogService _bookCatalogService;
    private readonly IBookManagementService _bookManagementService;
    private readonly IBookCoverGenerator _bookCoverGenerator;
    private readonly IAppFeedbackService _feedbackService;
    private readonly INavigationService _navigationService;
    private BookImportAnalysis? _pendingAnalysis;
    private CancellationTokenSource? _activeImportCancellationTokenSource;
    private CancellationTokenSource? _searchDebounceCancellationTokenSource;
    private IReadOnlyList<LibraryBookItemViewModel> _allBooks = [];
    private string? _activePlaybackBookId;
    private int _searchVersion;
    private bool _isDeletingBook;

    public LibraryViewModel(
        IBookImportService bookImportService,
        IBookCatalogService bookCatalogService,
        IBookManagementService bookManagementService,
        IBookCoverGenerator bookCoverGenerator,
        IAppFeedbackService feedbackService,
        INavigationService navigationService,
        IPlaybackCoordinator playbackCoordinator,
        LibraryScrollState scrollState)
    {
        _bookImportService = bookImportService;
        _bookCatalogService = bookCatalogService;
        _bookManagementService = bookManagementService;
        _bookCoverGenerator = bookCoverGenerator;
        _feedbackService = feedbackService;
        _navigationService = navigationService;
        ScrollState = scrollState;
        ApplyPlaybackSnapshot(playbackCoordinator.CurrentSnapshot);
        playbackCoordinator.SnapshotChanged += OnPlaybackSnapshotChanged;
    }

    public ObservableCollection<LibraryBookItemViewModel> Books { get; } = [];

    public IReadOnlyList<LibrarySortOption> AvailableSortOptions => SortOptions;

    public LibraryScrollState ScrollState { get; }

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isImportPanelVisible;

    [ObservableProperty]
    private bool isEncodingPreviewVisible;

    [ObservableProperty]
    private bool hasBooks;

    [ObservableProperty]
    private bool hasVisibleBooks;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private LibrarySortMode selectedSortMode = LibrarySortMode.RecentReading;

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

    [ObservableProperty]
    private string suggestedTitle = string.Empty;

    [ObservableProperty]
    private string? suggestedAuthor;

    [ObservableProperty]
    private bool isFileNameTemplateMatched;

    public string? LastImportedPath { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var books = await _bookCatalogService.GetBooksAsync(cancellationToken);
        _allBooks = books
            .Select(MapBook)
            .ToArray();
        UpdateDeleteAvailability();
        ApplyVisibleBooks();
    }

    public async Task ImportFileAsync(string filePath, CancellationToken cancellationToken)
    {
        LastImportedPath = filePath;
        IsImportPanelVisible = true;
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
                ClearPendingAnalysis(hidePanel: true);
                StatusMessage = string.Empty;
                _feedbackService.ShowSuccess("导入成功", $"已导入《{result.Title}》。");
            },
            cancellationToken);
    }

    [RelayCommand]
    private void CancelImport()
    {
        _activeImportCancellationTokenSource?.Cancel();
    }

    [RelayCommand]
    private void OpenBook(LibraryBookItemViewModel? book)
    {
        if (book is null)
        {
            return;
        }

        _navigationService.NavigateWithHierarchy(typeof(PlayerPage), new PlayerNavigationRequest(book.BookId));
    }

    [RelayCommand]
    private void OpenBookDetails(LibraryBookItemViewModel? book)
    {
        if (book is null)
        {
            return;
        }

        _navigationService.NavigateWithHierarchy(typeof(BookDetailsPage), new BookDetailsNavigationRequest(book.BookId));
    }

    [RelayCommand]
    private async Task DeleteBookAsync(LibraryBookItemViewModel? book, CancellationToken cancellationToken)
    {
        if (book is null || _isDeletingBook)
        {
            return;
        }

        if (!book.CanDelete)
        {
            StatusMessage = "正在播放的书籍暂时不能从书库删除。";
            return;
        }

        var decision = await _feedbackService.ConfirmDeletionAsync(
            "删除书籍",
            $"确定删除《{book.Title}》及其阅读进度吗？音频缓存也会一并删除。",
            cancellationToken);

        if (decision != AppConfirmationDecision.Confirm)
        {
            return;
        }

        _isDeletingBook = true;
        try
        {
            var result = await _bookManagementService.DeleteAsync(
                new BookDeleteRequest(book.BookId, DeleteAudioCache: true),
                cancellationToken);

            if (result is null)
            {
                StatusMessage = "这本书已不存在，书库已刷新。";
                await LoadAsync(cancellationToken);
                return;
            }

            await LoadAsync(cancellationToken);
            StatusMessage = string.Empty;
            _feedbackService.ShowSuccess("删除成功", $"已删除《{book.Title}》。");
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("删除失败", projected);
        }
        finally
        {
            _isDeletingBook = false;
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    partial void OnSearchTextChanged(string value)
    {
        ScheduleFilterRefresh();
    }

    partial void OnSelectedSortModeChanged(LibrarySortMode value)
    {
        ApplyVisibleBooks();
    }

    private async Task AnalyzeAsync(BookImportRequest request, CancellationToken cancellationToken)
    {
        ClearPendingAnalysis();
        IsImportPanelVisible = true;
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
            if (_pendingAnalysis is null && string.IsNullOrWhiteSpace(PreviewText))
            {
                IsImportPanelVisible = false;
            }
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("导入失败", projected);
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
        SuggestedTitle = analysis.SuggestedTitle;
        SuggestedAuthor = analysis.SuggestedAuthor;
        IsFileNameTemplateMatched = analysis.IsFileNameTemplateMatched;
        IsImportPanelVisible = true;
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
            _ => "导入失败，请重试。"
        };
    }

    private void ClearPendingAnalysis(bool hidePanel = false)
    {
        _pendingAnalysis = null;
        CanConfirmImport = false;
        IsEncodingPreviewVisible = false;
        PreviewText = string.Empty;
        SuggestedTitle = string.Empty;
        SuggestedAuthor = null;
        IsFileNameTemplateMatched = false;
        ImportProgressPercent = 0;
        ImportProgressText = string.Empty;
        if (hidePanel)
        {
            IsImportPanelVisible = false;
            StatusMessage = string.Empty;
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

    private void ScheduleFilterRefresh()
    {
        _searchDebounceCancellationTokenSource?.Cancel();
        _searchDebounceCancellationTokenSource?.Dispose();
        _searchDebounceCancellationTokenSource = new CancellationTokenSource();
        var version = Interlocked.Increment(ref _searchVersion);
        _ = ApplyVisibleBooksAsync(version, _searchDebounceCancellationTokenSource.Token);
    }

    private async Task ApplyVisibleBooksAsync(int version, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
            if (version != Volatile.Read(ref _searchVersion))
            {
                return;
            }

            ApplyVisibleBooks();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyVisibleBooks()
    {
        var normalizedSearchTerm = LibraryBookItemViewModel.NormalizeSearchText(SearchText);
        var filteredBooks = _allBooks.Where(book => book.MatchesSearch(normalizedSearchTerm));

        filteredBooks = SelectedSortMode switch
        {
            LibrarySortMode.Title => filteredBooks
                .OrderBy(static book => book.SortTitleKey, StringComparer.Ordinal)
                .ThenBy(static book => book.BookId, StringComparer.Ordinal),
            _ => filteredBooks
                .OrderByDescending(static book => book.HasReadingProgress)
                .ThenByDescending(static book => book.LastPlayedAt, StringComparer.Ordinal)
                .ThenBy(static book => book.SortTitleKey, StringComparer.Ordinal)
                .ThenBy(static book => book.BookId, StringComparer.Ordinal)
        };

        var visibleBooks = filteredBooks.ToArray();
        Books.ReplaceWith(visibleBooks, static book => book);
        HasBooks = _allBooks.Count > 0;
        HasVisibleBooks = visibleBooks.Length > 0;
    }

    private LibraryBookItemViewModel MapBook(BookSummary book)
    {
        return new LibraryBookItemViewModel(
            book.Id,
            book.Title,
            string.IsNullOrWhiteSpace(book.Author) ? "未知作者" : book.Author.Trim(),
            book.CurrentChapterTitle,
            BuildRemainingChapterText(book),
            book.OverallProgress,
            book.HasReadingProgress,
            book.LastPlayedAt,
            _bookCoverGenerator.Generate(book.Title),
            !string.Equals(book.Id, _activePlaybackBookId, StringComparison.Ordinal));
    }

    private static string BuildRemainingChapterText(BookSummary book)
    {
        return book.TotalChapterCount > 0 && book.RemainingChapterCount <= 0
            ? "最后一章"
            : $"剩余 {Math.Max(0, book.RemainingChapterCount)} 章";
    }

    private void OnPlaybackSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() => ApplyPlaybackSnapshot(snapshot));
            return;
        }

        ApplyPlaybackSnapshot(snapshot);
    }

    private void ApplyPlaybackSnapshot(PlaybackSnapshot snapshot)
    {
        _activePlaybackBookId = snapshot.BookId;
        UpdateDeleteAvailability();
    }

    private void UpdateDeleteAvailability()
    {
        foreach (var book in _allBooks)
        {
            book.CanDelete = !string.Equals(book.BookId, _activePlaybackBookId, StringComparison.Ordinal);
        }
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
