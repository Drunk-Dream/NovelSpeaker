using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Dialogs;
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

    private readonly IBookCatalogService _bookCatalogService;
    private readonly IBookManagementService _bookManagementService;
    private readonly IBookCoverGenerator _bookCoverGenerator;
    private readonly IImportBookDialogService _importBookDialogService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly INavigationService _navigationService;
    private CancellationTokenSource? _searchDebounceCancellationTokenSource;
    private IReadOnlyList<LibraryBookItemViewModel> _allBooks = [];
    private string? _activePlaybackBookId;
    private int _searchVersion;
    private bool _isDeletingBook;

    public LibraryViewModel(
        IBookCatalogService bookCatalogService,
        IBookManagementService bookManagementService,
        IBookCoverGenerator bookCoverGenerator,
        IImportBookDialogService importBookDialogService,
        IAppFeedbackService feedbackService,
        INavigationService navigationService,
        IPlaybackCoordinator playbackCoordinator,
        LibraryScrollState scrollState)
    {
        _bookCatalogService = bookCatalogService;
        _bookManagementService = bookManagementService;
        _bookCoverGenerator = bookCoverGenerator;
        _importBookDialogService = importBookDialogService;
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
    private bool hasBooks;

    [ObservableProperty]
    private bool hasVisibleBooks;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private LibrarySortMode selectedSortMode = LibrarySortMode.RecentReading;

    [ObservableProperty]
    private ContinueListeningItemViewModel? continueListening;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var booksTask = _bookCatalogService.GetBooksAsync(cancellationToken);
        var continueListeningTask = _bookCatalogService.GetContinueListeningAsync(cancellationToken);
        await Task.WhenAll(booksTask, continueListeningTask);

        var books = await booksTask;
        _allBooks = books
            .Select(MapBook)
            .ToArray();
        ContinueListening = MapContinueListening(await continueListeningTask);
        UpdateDeleteAvailability();
        ApplyVisibleBooks();
    }

    public async Task ImportFilesAsync(IReadOnlyList<string>? filePaths, CancellationToken cancellationToken)
    {
        var validatedPath = ValidateSingleImportFile(filePaths);
        if (validatedPath is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var outcome = await _importBookDialogService.ShowAsync(validatedPath, cancellationToken);
            if (outcome != ImportBookDialogOutcome.Imported)
            {
                return;
            }

            await LoadAsync(cancellationToken);
            _feedbackService.ShowSuccess("导入成功", "已导入小说。");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenBook(LibraryBookItemViewModel? book)
    {
        if (book is null)
        {
            return;
        }

        _navigationService.NavigateWithHierarchy(
            typeof(PlayerPage),
            new PlayerNavigationRequest(book.BookId, PlayerNavigationMode.OpenPaused));
    }

    [RelayCommand]
    private void OpenContinueListening()
    {
        if (ContinueListening is null)
        {
            return;
        }

        _navigationService.NavigateWithHierarchy(
            typeof(PlayerPage),
            new PlayerNavigationRequest(ContinueListening.BookId, PlayerNavigationMode.OpenPaused));
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

    private ContinueListeningItemViewModel? MapContinueListening(ContinueListeningSummary? summary)
    {
        if (summary is null)
        {
            return null;
        }

        return new ContinueListeningItemViewModel(
            summary.BookId,
            summary.BookTitle,
            summary.ChapterTitle,
            BuildRemainingChapterText(summary.TotalChapterCount, summary.RemainingChapterCount),
            summary.OverallProgress,
            _bookCoverGenerator.Generate(summary.BookTitle));
    }

    private static string BuildRemainingChapterText(BookSummary book)
    {
        return BuildRemainingChapterText(book.TotalChapterCount, book.RemainingChapterCount);
    }

    private static string BuildRemainingChapterText(int totalChapterCount, int remainingChapterCount)
    {
        return totalChapterCount > 0 && remainingChapterCount <= 0
            ? "最后一章"
            : $"剩余 {Math.Max(0, remainingChapterCount)} 章";
    }

    private string? ValidateSingleImportFile(IReadOnlyList<string>? filePaths)
    {
        if (filePaths is null || filePaths.Count == 0)
        {
            _feedbackService.ShowWarning("无法导入", "未检测到可导入的 TXT 文件。");
            return null;
        }

        if (filePaths.Count != 1)
        {
            _feedbackService.ShowWarning("无法导入", "一次只能导入一个 TXT 文件。");
            return null;
        }

        var filePath = filePaths[0];
        if (string.IsNullOrWhiteSpace(filePath) ||
            Directory.Exists(filePath) ||
            !File.Exists(filePath) ||
            !string.Equals(Path.GetExtension(filePath), ".txt", StringComparison.OrdinalIgnoreCase))
        {
            _feedbackService.ShowWarning("无法导入", "只支持导入单个 .txt 文件。");
            return null;
        }

        return filePath;
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
}
