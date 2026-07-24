using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.Cache;

public sealed partial class CacheManagementViewModel : ObservableObject
{
    private const string CleanupImpactMessage = "此操作只会清理音频缓存，不会删除书籍、章节、阅读进度、TTS 规则或章节规则。";

    private readonly ICacheWorkspaceService _cacheWorkspaceService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly IAppNavigator _navigator;
    private CancellationTokenSource? _chapterLoadCts;
    private int _chapterLoadVersion;
    private string? _selectedBookId;

    public CacheManagementViewModel(
        ICacheWorkspaceService cacheWorkspaceService,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        IAppNavigator navigator)
    {
        _cacheWorkspaceService = cacheWorkspaceService;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _navigator = navigator;
    }

    public ObservableCollection<CachedBookListItemViewModel> Books { get; } = [];

    public ObservableCollection<CachedChapterListItemViewModel> Chapters { get; } = [];

    [ObservableProperty]
    private bool isLoadingBooks;

    [ObservableProperty]
    private bool isLoadingChapters;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasSelection;

    [ObservableProperty]
    private bool selectedBookHasCache;

    [ObservableProperty]
    private string selectedBookTitle = string.Empty;

    [ObservableProperty]
    private string selectedBookAuthor = "未知作者";

    [ObservableProperty]
    private string selectedBookCacheSizeText = "0 B";

    [ObservableProperty]
    private string selectedBookChapterCountText = string.Empty;

    public bool HasBooks => Books.Count > 0;

    public bool ShowSelectionPrompt => !HasSelection;

    public bool ShowSelectedBookEmptyState => HasSelection && !SelectedBookHasCache && !IsLoadingChapters;

    public bool ShowSelectedBookContent => HasSelection && SelectedBookHasCache;

    public bool CanClearAll => !IsBusy && Books.Count > 0;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await LoadBooksAsync(cancellationToken);
        ClearSelection();
    }

    public void HandleNavigatedFrom()
    {
        CancelChapterLoad();
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (!await _navigator.GoBackAsync(cancellationToken).ConfigureAwait(true))
        {
            await _navigator.NavigateAsync(AppRoutes.CacheAndData, cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task SelectBookAsync(CachedBookListItemViewModel? item, CancellationToken cancellationToken)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        _selectedBookId = item.BookId;
        SelectedBookTitle = item.Title;
        SelectedBookAuthor = item.Author;
        SelectedBookCacheSizeText = item.CacheSizeText;
        SelectedBookChapterCountText = item.ChapterCountText;
        HasSelection = true;
        SelectedBookHasCache = true;
        UpdateBookSelection(item.BookId);
        NotifyVisibilityStateChanged();

        await LoadChaptersAsync(item.BookId, cancellationToken);
    }

    [RelayCommand]
    private async Task ClearAllAsync(CancellationToken cancellationToken)
    {
        if (!CanClearAll)
        {
            return;
        }

        var decision = await _dialogService.ShowConfirmationAsync(
            "清理全部缓存",
            $"将清理全部音频缓存。{CleanupImpactMessage}",
            "清理全部",
            "取消",
            cancellationToken);
        if (decision != AppConfirmationDecision.Confirm)
        {
            return;
        }

        await ExecuteCleanupAsync(
            ct => _cacheWorkspaceService.ClearAllAsync(ct),
            reloadSelectedBook: HasSelection,
            cancellationToken);
    }

    [RelayCommand]
    private async Task ClearBookAsync(CancellationToken cancellationToken)
    {
        if (!HasSelection || string.IsNullOrWhiteSpace(_selectedBookId) || IsBusy)
        {
            return;
        }

        var decision = await _dialogService.ShowConfirmationAsync(
            "清理本书缓存",
            $"将清理《{SelectedBookTitle}》的音频缓存。{CleanupImpactMessage}",
            "清理本书",
            "取消",
            cancellationToken);
        if (decision != AppConfirmationDecision.Confirm)
        {
            return;
        }

        var selectedBookId = _selectedBookId;
        await ExecuteCleanupAsync(
            ct => _cacheWorkspaceService.ClearBookAsync(selectedBookId!, ct),
            reloadSelectedBook: true,
            cancellationToken);
    }

    [RelayCommand]
    private async Task ClearChapterAsync(CachedChapterListItemViewModel? item, CancellationToken cancellationToken)
    {
        if (item is null || string.IsNullOrWhiteSpace(_selectedBookId) || IsBusy)
        {
            return;
        }

        var decision = await _dialogService.ShowConfirmationAsync(
            "清理本章缓存",
            $"将清理“{item.Title}”的音频缓存。{CleanupImpactMessage}",
            "清理本章",
            "取消",
            cancellationToken);
        if (decision != AppConfirmationDecision.Confirm)
        {
            return;
        }

        await ExecuteCleanupAsync(
            ct => _cacheWorkspaceService.ClearChapterAsync(item.BookId, item.ChapterIndex, ct),
            reloadSelectedBook: true,
            cancellationToken);
    }

    private async Task ExecuteCleanupAsync(
        Func<CancellationToken, Task<CacheCleanupResult>> cleanupAsync,
        bool reloadSelectedBook,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        NotifyCommandStateChanged();

        try
        {
            var result = await cleanupAsync(cancellationToken);
            await LoadBooksAsync(cancellationToken);

            if (reloadSelectedBook && !string.IsNullOrWhiteSpace(_selectedBookId))
            {
                var selectedBook = Books.FirstOrDefault(book => string.Equals(book.BookId, _selectedBookId, StringComparison.Ordinal));
                if (selectedBook is not null)
                {
                    SelectedBookTitle = selectedBook.Title;
                    SelectedBookAuthor = selectedBook.Author;
                    SelectedBookCacheSizeText = selectedBook.CacheSizeText;
                    SelectedBookChapterCountText = selectedBook.ChapterCountText;
                    SelectedBookHasCache = true;
                    UpdateBookSelection(selectedBook.BookId);
                    await LoadChaptersAsync(selectedBook.BookId, cancellationToken);
                }
                else
                {
                    Chapters.Clear();
                    SelectedBookHasCache = false;
                    UpdateBookSelection(null);
                    NotifyVisibilityStateChanged();
                }
            }
            else if (Books.Count == 0)
            {
                ClearSelection();
            }

            ShowCleanupFeedback(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("清理缓存失败", _feedbackService.Project(exception));
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task LoadBooksAsync(CancellationToken cancellationToken)
    {
        IsLoadingBooks = true;
        try
        {
            var books = await _cacheWorkspaceService.GetCachedBooksAsync(cancellationToken);
            Books.Clear();
            foreach (var book in books)
            {
                Books.Add(new CachedBookListItemViewModel(
                    book.BookId,
                    book.Title,
                    book.Author,
                    CacheCleanupFeedbackFormatter.FormatBytes(book.TotalSizeBytes),
                    $"已缓存 {book.ChapterCount} 章"));
            }

            UpdateBookSelection(_selectedBookId);
            NotifyVisibilityStateChanged();
            NotifyCommandStateChanged();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("加载缓存书籍失败", _feedbackService.Project(exception));
        }
        finally
        {
            IsLoadingBooks = false;
        }
    }

    private async Task LoadChaptersAsync(string bookId, CancellationToken cancellationToken)
    {
        CancelChapterLoad();
        _chapterLoadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var localCts = _chapterLoadCts;
        var version = Interlocked.Increment(ref _chapterLoadVersion);

        IsLoadingChapters = true;
        Chapters.Clear();
        NotifyVisibilityStateChanged();

        try
        {
            var chapters = await _cacheWorkspaceService.GetCachedChaptersAsync(bookId, localCts.Token);
            if (version != Volatile.Read(ref _chapterLoadVersion) ||
                !string.Equals(bookId, _selectedBookId, StringComparison.Ordinal))
            {
                return;
            }

            Chapters.Clear();
            foreach (var chapter in chapters)
            {
                Chapters.Add(new CachedChapterListItemViewModel(
                    chapter.BookId,
                    chapter.ChapterIndex,
                    $"第 {chapter.ChapterIndex + 1} 章",
                    chapter.Title,
                    CacheCleanupFeedbackFormatter.FormatBytes(chapter.TotalSizeBytes),
                    $"{chapter.EntryCount} 条缓存",
                    FormatCompleteness(chapter)));
            }

            SelectedBookHasCache = Chapters.Count > 0;
            NotifyVisibilityStateChanged();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref _chapterLoadVersion))
            {
                _feedbackService.ShowProjectedNotification("加载章节缓存失败", _feedbackService.Project(exception));
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _chapterLoadVersion))
            {
                IsLoadingChapters = false;
                NotifyVisibilityStateChanged();
            }
        }
    }

    private void ClearSelection()
    {
        _selectedBookId = null;
        HasSelection = false;
        SelectedBookHasCache = false;
        SelectedBookTitle = string.Empty;
        SelectedBookAuthor = "未知作者";
        SelectedBookCacheSizeText = "0 B";
        SelectedBookChapterCountText = string.Empty;
        Chapters.Clear();
        UpdateBookSelection(null);
        NotifyVisibilityStateChanged();
    }

    private void UpdateBookSelection(string? selectedBookId)
    {
        foreach (var book in Books)
        {
            book.IsSelected = !string.IsNullOrWhiteSpace(selectedBookId) &&
                              string.Equals(book.BookId, selectedBookId, StringComparison.Ordinal);
        }
    }

    private void CancelChapterLoad()
    {
        _chapterLoadCts?.Cancel();
        _chapterLoadCts?.Dispose();
        _chapterLoadCts = null;
    }

    private void NotifyVisibilityStateChanged()
    {
        OnPropertyChanged(nameof(HasBooks));
        OnPropertyChanged(nameof(ShowSelectionPrompt));
        OnPropertyChanged(nameof(ShowSelectedBookEmptyState));
        OnPropertyChanged(nameof(ShowSelectedBookContent));
    }

    private void NotifyCommandStateChanged()
    {
        OnPropertyChanged(nameof(CanClearAll));
    }

    private void ShowCleanupFeedback(CacheCleanupResult result)
    {
        var feedback = CacheCleanupFeedbackFormatter.Format(result, "缓存已清理", "缓存已部分清理");
        if (feedback.IsWarning)
        {
            _feedbackService.ShowWarning(feedback.Title, feedback.Message);
        }
        else
        {
            _feedbackService.ShowSuccess(feedback.Title, feedback.Message);
        }
    }

    private static string FormatCompleteness(CachedChapterCacheItem chapter)
    {
        if (chapter.EstimatedTotalSegmentCount is null || chapter.EstimatedTotalSegmentCount <= 0)
        {
            return $"已缓存 {chapter.CachedSegmentCount} 段";
        }

        var ratio = Math.Clamp(chapter.CachedSegmentCount / (double)chapter.EstimatedTotalSegmentCount.Value, 0, 1);
        return $"已缓存 {chapter.CachedSegmentCount}/{chapter.EstimatedTotalSegmentCount.Value} 段 · {ratio:P0}";
    }
}
