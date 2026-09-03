using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.Cache;

public sealed partial class CacheManagementViewModel : ObservableObject
{
    private const string CleanupImpactMessage = "此操作只会清理音频缓存，不会删除书籍、章节、阅读进度、TTS 规则或章节规则。";

    private readonly ICacheWorkspaceService _cacheWorkspaceService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly IAppNavigator _navigator;
    private readonly IChapterExportCoordinator _chapterExportCoordinator;
    private readonly IPresentationFileDialogService _fileDialogs;
    private readonly IUiScheduler _uiScheduler;
    private readonly DesktopSelectionController<int> _chapterSelection = new();
    private readonly OwnedTaskRegistry _pageTasks = new();
    private readonly object _cacheRefreshSync = new();
    private CancellationTokenSource? _chapterLoadCts;
    private CancellationTokenSource? _exportPreparationCts;
    private CancellationTokenSource? _pageCancellation;
    private int _bookLoadVersion;
    private int _chapterLoadVersion;
    private int _cacheRefreshGeneration;
    private bool _isPageActive;
    private bool _isCacheEventsRegistered;
    private bool _isExportEventsRegistered;
    private bool _isCacheRefreshRunning;
    private bool _cacheRefreshPending;
    private string? _cacheRefreshBookId;
    private string? _selectedBookId;

    public CacheManagementViewModel(
        ICacheWorkspaceService cacheWorkspaceService,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        IAppNavigator navigator,
        IChapterExportCoordinator chapterExportCoordinator,
        IPresentationFileDialogService fileDialogs,
        IUiScheduler? uiScheduler = null)
    {
        _cacheWorkspaceService = cacheWorkspaceService;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _navigator = navigator;
        _chapterExportCoordinator = chapterExportCoordinator;
        _fileDialogs = fileDialogs;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        _chapterSelection.SelectionChanged += OnChapterSelectionChanged;
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

    public bool ShowSelectedBookContent => HasSelection && SelectedBookHasCache && !IsLoadingChapters;

    public IReadOnlyList<int> SelectedChapterIndices => _chapterSelection.SelectedItems;

    public string ChapterSelectionSummary => $"已选择 {_chapterSelection.Count} 章";

    public bool CanClearSelectedChapters =>
        !IsBusy &&
        HasSelection &&
        !string.IsNullOrWhiteSpace(_selectedBookId) &&
        _chapterSelection.Count > 0;

    public bool CanExportSelectedChapters =>
        !IsBusy &&
        !IsChapterExportActive() &&
        HasSelection &&
        !string.IsNullOrWhiteSpace(_selectedBookId) &&
        _chapterSelection.Count > 0;

    public string ExportCommandToolTip
    {
        get
        {
            if (IsChapterExportActive())
            {
                return "已有章节导出任务正在运行";
            }

            if (_chapterSelection.Count == 0)
            {
                return "请先选择要导出的章节";
            }

            return SelectedChaptersAreExportable()
                ? "将所选章节导出为 MP3"
                : "导出可用章节；不可导出章节将先请求确认";
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        ActivatePage(cancellationToken);
        await LoadBooksAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ClearSelection();
    }

    public void HandleNavigatedFrom()
    {
        Interlocked.Increment(ref _bookLoadVersion);
        Interlocked.Increment(ref _chapterLoadVersion);
        CancelChapterLoad();
        IsLoadingChapters = false;
        CancelExportPreparation();
        DeactivatePage();
        _chapterSelection.Clear();
        NotifyVisibilityStateChanged();
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (!await _navigator.NavigateBackAsync(cancellationToken).ConfigureAwait(true))
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

        _chapterSelection.Clear();
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

    public void HandleChapterClick(
        CachedChapterListItemViewModel? item,
        DesktopSelectionModifiers modifiers)
    {
        if (item is null ||
            IsBusy ||
            !string.Equals(item.BookId, _selectedBookId, StringComparison.Ordinal))
        {
            return;
        }

        _chapterSelection.Click(item.ChapterIndex, modifiers);
    }

    public bool HandleSelectAllChapters()
    {
        if (!HasSelection || IsBusy || Chapters.Count == 0)
        {
            return false;
        }

        _chapterSelection.SelectAll();
        return true;
    }

    public bool HandleClearChapterSelection()
    {
        if (_chapterSelection.Count == 0)
        {
            return false;
        }

        _chapterSelection.Clear();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanClearSelectedChapters), AllowConcurrentExecutions = false)]
    private async Task ClearSelectedChaptersAsync(CancellationToken cancellationToken)
    {
        if (!CanClearSelectedChapters || string.IsNullOrWhiteSpace(_selectedBookId))
        {
            return;
        }

        var selectedBookId = _selectedBookId;
        var selectedIndices = SelectedChapterIndices.ToArray();
        var decision = await _dialogService.ShowConfirmationAsync(
            "清理所选章节缓存",
            $"将清理《{SelectedBookTitle}》中选定的 {selectedIndices.Length} 章音频缓存。{CleanupImpactMessage}",
            "清理",
            "取消",
            cancellationToken);
        if (decision != AppConfirmationDecision.Confirm)
        {
            return;
        }

        await ExecuteCleanupAsync(
            ct => _cacheWorkspaceService.ClearChaptersAsync(selectedBookId, selectedIndices, ct),
            reloadSelectedBook: true,
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanExportSelectedChapters), AllowConcurrentExecutions = false)]
    private async Task ExportSelectedChaptersAsync(CancellationToken cancellationToken)
    {
        if (!CanExportSelectedChapters || string.IsNullOrWhiteSpace(_selectedBookId))
        {
            return;
        }

        var selectedBookId = _selectedBookId;
        var selectedChapters = Chapters
            .Where(chapter => _chapterSelection.IsSelected(chapter.ChapterIndex))
            .OrderBy(chapter => chapter.ChapterIndex)
            .ToArray();
        var exportableChapters = selectedChapters
            .Where(chapter => chapter.IsExportable)
            .ToArray();
        var skippedChapterCount = selectedChapters.Length - exportableChapters.Length;

        if (exportableChapters.Length == 0)
        {
            _feedbackService.ShowWarning(
                "没有可导出的章节",
                "所选章节当前均不可导出，请先完成缓存后重试。");
            return;
        }

        var operationCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _pageCancellation?.Token ?? CancellationToken.None);
        if (Interlocked.CompareExchange(ref _exportPreparationCts, operationCts, null) is not null)
        {
            operationCts.Dispose();
            return;
        }

        IsBusy = true;
        NotifyCommandStateChanged();

        try
        {
            if (skippedChapterCount > 0)
            {
                var decision = await _dialogService.ShowConfirmationAsync(
                    "跳过不可导出章节",
                    $"所选 {selectedChapters.Length} 章中有 {skippedChapterCount} 章当前不可导出。" +
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

            var startResult = await _chapterExportCoordinator.StartAsync(
                new StartChapterExportRequest(
                    selectedBookId,
                    SelectedBookTitle,
                    exportableChapters
                        .Select(chapter => new ChapterExportSelection(chapter.ChapterIndex, chapter.Title))
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
            Interlocked.CompareExchange(ref _exportPreparationCts, null, operationCts);
            operationCts.Dispose();
            IsBusy = false;
            NotifyCommandStateChanged();
        }
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
            _feedbackService.ShowProjectedNotification("清理失败", _feedbackService.Project(exception));
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task LoadBooksAsync(CancellationToken cancellationToken)
    {
        var version = Interlocked.Increment(ref _bookLoadVersion);
        IsLoadingBooks = true;
        try
        {
            var books = await _cacheWorkspaceService.GetCachedBooksAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _bookLoadVersion))
            {
                return;
            }

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
            if (version == Volatile.Read(ref _bookLoadVersion))
            {
                IsLoadingBooks = false;
            }
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
        _chapterSelection.SetItems([]);
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
                var exportAvailability = GetExportAvailability(chapter);
                Chapters.Add(new CachedChapterListItemViewModel(
                    chapter.BookId,
                    chapter.ChapterIndex,
                    $"第 {chapter.ChapterIndex + 1} 章",
                    chapter.Title,
                    CacheCleanupFeedbackFormatter.FormatBytes(chapter.TotalSizeBytes),
                    $"{chapter.EntryCount} 条缓存",
                    CacheManagementCompletenessFormatter.Format(chapter),
                    exportAvailability.IsExportable,
                    exportAvailability.StatusText,
                    exportAvailability.ToolTip));
            }

            _chapterSelection.SetItems(Chapters.Select(chapter => chapter.ChapterIndex));
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
        IsLoadingChapters = false;
        SelectedBookTitle = string.Empty;
        SelectedBookAuthor = "未知作者";
        SelectedBookCacheSizeText = "0 B";
        SelectedBookChapterCountText = string.Empty;
        Chapters.Clear();
        _chapterSelection.SetItems([]);
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

    private void ActivatePage(CancellationToken cancellationToken)
    {
        DeactivatePage();
        _pageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_cacheRefreshSync)
        {
            _isPageActive = true;
            _cacheRefreshGeneration++;
            _cacheRefreshPending = false;
            _cacheRefreshBookId = null;
            _isCacheRefreshRunning = false;
        }

        _cacheWorkspaceService.Changed += OnCacheChanged;
        _isCacheEventsRegistered = true;
        _chapterExportCoordinator.SnapshotChanged += OnChapterExportSnapshotChanged;
        _isExportEventsRegistered = true;
    }

    private void DeactivatePage()
    {
        if (_isCacheEventsRegistered)
        {
            _cacheWorkspaceService.Changed -= OnCacheChanged;
            _isCacheEventsRegistered = false;
        }

        if (_isExportEventsRegistered)
        {
            _chapterExportCoordinator.SnapshotChanged -= OnChapterExportSnapshotChanged;
            _isExportEventsRegistered = false;
        }

        CancellationTokenSource? pageCancellation;
        lock (_cacheRefreshSync)
        {
            pageCancellation = _pageCancellation;
            _pageCancellation = null;
            _isPageActive = false;
            _cacheRefreshGeneration++;
            _cacheRefreshPending = false;
            _cacheRefreshBookId = null;
            _isCacheRefreshRunning = false;
        }

        pageCancellation?.Cancel();
        pageCancellation?.Dispose();
    }

    private void OnCacheChanged(object? sender, CacheChangedEventArgs eventArgs)
    {
        var selectedBookId = _selectedBookId;
        if (!_isCacheEventsRegistered ||
            string.IsNullOrWhiteSpace(selectedBookId) ||
            (!string.IsNullOrWhiteSpace(eventArgs.BookId) &&
             !string.Equals(eventArgs.BookId, selectedBookId, StringComparison.Ordinal)))
        {
            return;
        }

        if (!TryGetActivePageCancellationToken(out var cancellationToken))
        {
            return;
        }

        if (!_uiScheduler.CheckAccess())
        {
            try
            {
                _pageTasks.Register(
                    _uiScheduler.InvokeAsync(
                        () => QueueCacheRefresh(selectedBookId),
                        cancellationToken),
                    ReportCacheRefreshFailure);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ReportCacheRefreshFailure(exception);
            }

            return;
        }

        QueueCacheRefresh(selectedBookId);
    }

    private void QueueCacheRefresh(string bookId)
    {
        int generation;
        CancellationToken cancellationToken;
        lock (_cacheRefreshSync)
        {
            if (!_isPageActive ||
                _pageCancellation is not { IsCancellationRequested: false } pageCancellation ||
                !string.Equals(bookId, _selectedBookId, StringComparison.Ordinal))
            {
                return;
            }

            _cacheRefreshPending = true;
            _cacheRefreshBookId = bookId;
            if (_isCacheRefreshRunning)
            {
                return;
            }

            _isCacheRefreshRunning = true;
            generation = _cacheRefreshGeneration;
            cancellationToken = pageCancellation.Token;
        }

        _pageTasks.Register(
            RefreshChangedChaptersAsync(generation, cancellationToken),
            ReportCacheRefreshFailure);
    }

    private async Task RefreshChangedChaptersAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                string bookId;
                lock (_cacheRefreshSync)
                {
                    if (generation != _cacheRefreshGeneration || !_isPageActive)
                    {
                        return;
                    }

                    if (!_cacheRefreshPending || string.IsNullOrWhiteSpace(_cacheRefreshBookId))
                    {
                        _isCacheRefreshRunning = false;
                        return;
                    }

                    _cacheRefreshPending = false;
                    bookId = _cacheRefreshBookId;
                }

                await LoadChaptersAsync(bookId, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_cacheRefreshSync)
            {
                if (generation == _cacheRefreshGeneration)
                {
                    _isCacheRefreshRunning = false;
                }
            }
        }
    }

    private void ReportCacheRefreshFailure(Exception exception)
    {
        lock (_cacheRefreshSync)
        {
            if (!_isPageActive)
            {
                return;
            }
        }

        _feedbackService.ShowProjectedNotification(
            "刷新缓存管理列表失败",
            _feedbackService.Project(exception));
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
        OnPropertyChanged(nameof(CanClearSelectedChapters));
        OnPropertyChanged(nameof(CanExportSelectedChapters));
        OnPropertyChanged(nameof(ExportCommandToolTip));
        ClearSelectedChaptersCommand.NotifyCanExecuteChanged();
        ExportSelectedChaptersCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifyCommandStateChanged();
    }

    private void OnChapterSelectionChanged(
        object? sender,
        DesktopSelectionChangedEventArgs<int> e)
    {
        foreach (var chapter in Chapters)
        {
            chapter.IsSelected = _chapterSelection.IsSelected(chapter.ChapterIndex);
        }

        OnPropertyChanged(nameof(SelectedChapterIndices));
        OnPropertyChanged(nameof(ChapterSelectionSummary));
        NotifyCommandStateChanged();
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

    private bool SelectedChaptersAreExportable()
    {
        var selectedIndices = _chapterSelection.SelectedItems;
        if (selectedIndices.Count == 0)
        {
            return false;
        }

        var chaptersByIndex = Chapters.ToDictionary(chapter => chapter.ChapterIndex);
        return selectedIndices.All(
            index => chaptersByIndex.TryGetValue(index, out var chapter) && chapter.IsExportable);
    }

    private void CancelExportPreparation()
    {
        _exportPreparationCts?.Cancel();
    }

    private void OnChapterExportSnapshotChanged(object? sender, ChapterExportSnapshot snapshot)
    {
        if (!_isExportEventsRegistered)
        {
            return;
        }

        if (!TryGetActivePageCancellationToken(out var cancellationToken))
        {
            return;
        }

        if (!_uiScheduler.CheckAccess())
        {
            try
            {
                _pageTasks.Register(
                    _uiScheduler.InvokeAsync(NotifyCommandStateChanged, cancellationToken),
                    ReportExportProjectionFailure);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ReportExportProjectionFailure(exception);
            }

            return;
        }

        NotifyCommandStateChanged();
    }

    private void ReportExportProjectionFailure(Exception exception)
    {
        lock (_cacheRefreshSync)
        {
            if (!_isPageActive)
            {
                return;
            }
        }

        _feedbackService.ShowProjectedNotification(
            "更新导出状态失败",
            _feedbackService.Project(exception));
    }

    private bool TryGetActivePageCancellationToken(out CancellationToken cancellationToken)
    {
        lock (_cacheRefreshSync)
        {
            if (!_isPageActive ||
                _pageCancellation is not { IsCancellationRequested: false } pageCancellation)
            {
                cancellationToken = default;
                return false;
            }

            cancellationToken = pageCancellation.Token;
            return true;
        }
    }

    private bool IsChapterExportActive() =>
        _chapterExportCoordinator.CurrentSnapshot?.Status is
            ChapterExportBatchStatus.Waiting or
            ChapterExportBatchStatus.Running or
            ChapterExportBatchStatus.Cancelling;

    private static ChapterExportAvailability GetExportAvailability(CachedChapterCacheItem chapter)
    {
        if (chapter.CurrentConfigurationSegmentCount is null)
        {
            return new ChapterExportAvailability(
                false,
                "当前配置不可用，无法导出",
                "无法读取当前 TTS 与文本配置对应的章节缓存。");
        }

        var total = chapter.CurrentConfigurationSegmentCount.Value;
        if (total == 0)
        {
            return new ChapterExportAvailability(
                false,
                "没有可播放段落，无法导出",
                "当前文本配置下没有可播放段落。");
        }

        if (chapter.CachedSegmentCount != total)
        {
            return new ChapterExportAvailability(
                false,
                "缓存不完整，无法导出",
                $"当前配置缓存为 {chapter.CachedSegmentCount}/{total} 段，请先完成缓存。");
        }

        return new ChapterExportAvailability(
            true,
            "可导出",
            $"当前配置缓存完整（{total}/{total} 段），可导出为 MP3。");
    }

    private sealed record ChapterExportAvailability(
        bool IsExportable,
        string StatusText,
        string ToolTip);
}
