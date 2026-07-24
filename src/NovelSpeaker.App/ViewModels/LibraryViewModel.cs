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

    private readonly IBookLibraryQuery _bookLibraryQuery;
    private readonly IBookDeletionService _bookDeletionService;
    private readonly IBookCoverGenerator _bookCoverGenerator;
    private readonly ILibraryImportCoordinator _libraryImportCoordinator;
    private readonly IBookDeleteDialogService _deleteDialogService;
    private readonly IBookCatalogInvalidationState _catalogInvalidationState;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppNavigator _navigator;
    private readonly IPlaybackBookCommands _playbackCoordinator;
    private CancellationTokenSource? _searchDebounceCancellationTokenSource;
    private IReadOnlyList<LibraryBookItemViewModel> _allBooks = [];
    private string? _activePlaybackBookId;
    private int _searchVersion;
    private int _importVersion;
    private bool _isDeletingBook;
    private bool _isPageEventsRegistered;
    private CancellationTokenSource? _activeImportCancellationTokenSource;

    public LibraryViewModel(
        IBookLibraryQuery bookLibraryQuery,
        IBookDeletionService bookDeletionService,
        IBookCoverGenerator bookCoverGenerator,
        ILibraryImportCoordinator libraryImportCoordinator,
        IBookDeleteDialogService deleteDialogService,
        IBookCatalogInvalidationState catalogInvalidationState,
        IAppFeedbackService feedbackService,
        IAppNavigator navigator,
        IPlaybackBookCommands playbackCoordinator,
        LibraryScrollState scrollState)
    {
        _bookLibraryQuery = bookLibraryQuery;
        _bookDeletionService = bookDeletionService;
        _bookCoverGenerator = bookCoverGenerator;
        _libraryImportCoordinator = libraryImportCoordinator;
        _deleteDialogService = deleteDialogService;
        _catalogInvalidationState = catalogInvalidationState;
        _feedbackService = feedbackService;
        _navigator = navigator;
        _playbackCoordinator = playbackCoordinator;
        ScrollState = scrollState;
        ApplyPlaybackSnapshot(playbackCoordinator.CurrentSnapshot);
        RegisterPageEvents();
    }

    public ObservableCollection<LibraryBookItemViewModel> Books { get; } = [];

    public IReadOnlyList<LibrarySortOption> AvailableSortOptions => SortOptions;

    public LibraryScrollState ScrollState { get; }

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string importStatusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasBooks;

    [ObservableProperty]
    private bool hasVisibleBooks;

    [ObservableProperty]
    private bool hasSearchText;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private LibrarySortMode selectedSortMode = LibrarySortMode.RecentReading;

    [ObservableProperty]
    private string librarySummaryText = "共 0 本 · 最近阅读优先";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var books = await _bookLibraryQuery.GetBooksAsync(cancellationToken);
        _allBooks = books
            .Select(MapBook)
            .ToArray();
        ApplyVisibleBooks();
        _catalogInvalidationState.Consume();
    }

    public async Task ImportFilesAsync(IReadOnlyList<string>? filePaths, CancellationToken cancellationToken)
    {
        var validatedPath = ValidateSingleImportFile(filePaths);
        if (validatedPath is null)
        {
            return;
        }

        var version = Interlocked.Increment(ref _importVersion);
        ReplaceActiveImport(cancellationToken);
        var activeCancellationTokenSource = _activeImportCancellationTokenSource!;
        var progress = new Progress<BookImportProgress>(update => ApplyImportProgress(version, activeCancellationTokenSource, update));
        IsBusy = true;
        ImportStatusMessage = "正在准备导入。";
        StatusMessage = string.Empty;
        try
        {
            var outcome = await _libraryImportCoordinator.ImportAsync(validatedPath, progress, activeCancellationTokenSource.Token);
            if (!IsCurrentImport(version, activeCancellationTokenSource))
            {
                return;
            }

            if (outcome.Status == LibraryImportCoordinatorStatus.Imported)
            {
                await LoadAsync(activeCancellationTokenSource.Token);
                _feedbackService.ShowSuccess("导入成功", "已导入小说。");
            }
            else if (outcome.Status == LibraryImportCoordinatorStatus.Failed)
            {
                ShowImportFailure(outcome.FailureReason);
            }
        }
        catch (OperationCanceledException) when (activeCancellationTokenSource.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_activeImportCancellationTokenSource, activeCancellationTokenSource))
            {
                _activeImportCancellationTokenSource = null;
            }

            activeCancellationTokenSource.Dispose();
            if (version == Volatile.Read(ref _importVersion))
            {
                IsBusy = false;
                ImportStatusMessage = string.Empty;
            }
        }
    }

    public void CancelActiveImport()
    {
        ReplaceActiveImport(CancellationToken.None, clearAfterCancel: true);
        ImportStatusMessage = string.Empty;
        IsBusy = false;
    }

    public void HandleNavigatedTo()
    {
        RegisterPageEvents();
        ApplyPlaybackSnapshot(_playbackCoordinator.CurrentSnapshot);
    }

    public void HandleNavigatedFrom()
    {
        CancelActiveImport();
        if (!_isPageEventsRegistered)
        {
            return;
        }

        _playbackCoordinator.SnapshotChanged -= OnPlaybackSnapshotChanged;
        _isPageEventsRegistered = false;
    }

    private void RegisterPageEvents()
    {
        if (_isPageEventsRegistered)
        {
            return;
        }

        _playbackCoordinator.SnapshotChanged += OnPlaybackSnapshotChanged;
        _isPageEventsRegistered = true;
    }

    [RelayCommand]
    private Task OpenBook(LibraryBookItemViewModel? book, CancellationToken cancellationToken)
    {
        if (book is null)
        {
            return Task.CompletedTask;
        }

        return _navigator.NavigateAsync(
            new PlayerRoute(book.BookId, PlayerNavigationMode.OpenPaused),
            cancellationToken);
    }

    [RelayCommand]
    private Task OpenBookDetails(LibraryBookItemViewModel? book, CancellationToken cancellationToken)
    {
        if (book is null)
        {
            return Task.CompletedTask;
        }

        return _navigator.NavigateAsync(new BookDetailsRoute(book.BookId), cancellationToken);
    }

    [RelayCommand]
    private async Task DeleteBookAsync(LibraryBookItemViewModel? book, CancellationToken cancellationToken)
    {
        if (book is null || _isDeletingBook)
        {
            return;
        }

        var decision = await _deleteDialogService.ShowAsync(
            new BookDeleteDialogRequest(
                book.Title,
                string.Equals(book.BookId, _activePlaybackBookId, StringComparison.Ordinal)),
            cancellationToken);
        if (!decision.IsConfirmed)
        {
            return;
        }

        _isDeletingBook = true;
        try
        {
            if (string.Equals(book.BookId, _activePlaybackBookId, StringComparison.Ordinal))
            {
                await _playbackCoordinator.HandleBookDeletedAsync(book.BookId, cancellationToken);
            }

            var result = await _bookDeletionService.DeleteAsync(
                new BookDeleteRequest(book.BookId, decision.DeleteAudioCache),
                cancellationToken);

            if (result is null)
            {
                StatusMessage = "这本书已不存在，书库已刷新。";
                _catalogInvalidationState.Invalidate();
                await LoadAsync(cancellationToken);
                return;
            }

            _catalogInvalidationState.Invalidate();
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
        HasSearchText = !string.IsNullOrWhiteSpace(value);
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
                .ThenByDescending(static book => book.LastPlayedAt)
                .ThenBy(static book => book.SortTitleKey, StringComparer.Ordinal)
                .ThenBy(static book => book.BookId, StringComparer.Ordinal)
        };

        var visibleBooks = filteredBooks.ToArray();
        Books.ReplaceWith(visibleBooks, static book => book);
        HasBooks = _allBooks.Count > 0;
        HasVisibleBooks = visibleBooks.Length > 0;
        LibrarySummaryText = BuildLibrarySummary(_allBooks.Count, SelectedSortMode);
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
            book.LastPlayedAt?.ToString("O"),
            _bookCoverGenerator.Generate(book.Title),
            canDelete: true);
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
    }

    private static string BuildLibrarySummary(int totalBooks, LibrarySortMode sortMode)
    {
        return sortMode == LibrarySortMode.Title
            ? $"共 {totalBooks} 本 · 按书名排序"
            : $"共 {totalBooks} 本 · 最近阅读优先";
    }

    private void ReplaceActiveImport(
        CancellationToken cancellationToken,
        bool clearAfterCancel = false)
    {
        _activeImportCancellationTokenSource?.Cancel();
        _activeImportCancellationTokenSource?.Dispose();
        _activeImportCancellationTokenSource = clearAfterCancel
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    private bool IsCurrentImport(int version, CancellationTokenSource activeCancellationTokenSource)
    {
        return version == Volatile.Read(ref _importVersion) &&
            ReferenceEquals(_activeImportCancellationTokenSource, activeCancellationTokenSource) &&
            !activeCancellationTokenSource.IsCancellationRequested;
    }

    private void ApplyImportProgress(
        int version,
        CancellationTokenSource activeCancellationTokenSource,
        BookImportProgress progress)
    {
        if (!IsCurrentImport(version, activeCancellationTokenSource))
        {
            return;
        }

        if (progress.IsIndeterminate || progress.TotalBytes <= 0)
        {
            ImportStatusMessage = progress.Message;
            return;
        }

        var percent = Math.Clamp(progress.BytesProcessed * 100d / progress.TotalBytes, 0, 100);
        ImportStatusMessage = $"{progress.Message} {percent:0.#}%";
    }

    private void ShowImportFailure(BookImportFailureReason? failureReason)
    {
        var message = failureReason switch
        {
            BookImportFailureReason.DuplicateBook => "该小说已经导入",
            BookImportFailureReason.NoValidChapters => "章节解析失败，请检查文件内容。",
            BookImportFailureReason.UnsupportedEncoding => "无法识别编码，请手动选择。",
            BookImportFailureReason.FileReadFailed => "文件无法读取，请确认文件仍可访问。",
            _ => "导入失败，请重试。"
        };

        _feedbackService.ShowWarning("无法导入", message);
    }
}
