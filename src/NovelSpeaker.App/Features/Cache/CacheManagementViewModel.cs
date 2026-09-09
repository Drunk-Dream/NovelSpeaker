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

public sealed partial class CacheManagementViewModel : ObservableObject, ITransientEscapeHandler
{
    private const string CleanupImpactMessage = "此操作只会清理音频缓存，不会删除书籍、章节、阅读进度、TTS 规则或章节规则。";
    private const int ChapterDecorationWindowSize = 32;
    private const int SelectionDecorationResetThreshold = 64;

    private readonly IAudioCacheStore _cacheStore;
    private readonly ICacheCatalog _cacheCatalog;
    private readonly ICacheCoverageQuery _cacheCoverageQuery;
    private readonly ICachePlanRepairRequestor? _cachePlanRepairRequestor;
    private readonly ICacheInvalidationCoordinator _invalidationCoordinator;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly IAppNavigator _navigator;
    private readonly IChapterExportCoordinator _chapterExportCoordinator;
    private readonly IPresentationFileDialogService _fileDialogs;
    private readonly IUiScheduler _uiScheduler;
    private readonly DesktopSelectionController<int> _chapterSelection = new();
    private readonly ResettableObservableCollection<CachedBookListItemViewModel> _books = [];
    private readonly ResettableObservableCollection<CachedChapterListItemViewModel> _chapters = [];
    private Dictionary<string, int> _bookPositions = new(StringComparer.Ordinal);
    private IndexedCatalog<CachedChapterCatalogItem> _chapterCatalog =
        new([], static chapter => chapter.ChapterIndex);
    private readonly SparseCatalogDecoration<bool> _chapterSelectionDecorations = new();
    private readonly SparseCatalogDecoration<CachedChapterDecoration> _chapterRefreshOverrides = new();
    private readonly HashSet<int> _chapterDecorationWindow = [];
    private readonly HashSet<int> _pendingChapterRowRefreshes = [];
    private readonly SemaphoreSlim _chapterDecorationQueryGate = new(1, 1);
    private readonly OwnedTaskRegistry _pageTasks = new();
    private readonly object _cacheRefreshSync = new();
    private readonly HashSet<int> _pendingCacheRefreshChapterIndices = [];
    private readonly HashSet<int> _pendingCacheRefreshCoverageIndices = [];
    private readonly HashSet<string> _pendingCacheRefreshBookIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _cacheRefreshBookEpochs = new(StringComparer.Ordinal);
    private CancellationTokenSource? _chapterLoadCts;
    private CancellationTokenSource? _chapterDecorationCts;
    private CancellationTokenSource? _bookProjectionCts;
    private CancellationTokenSource? _exportPreparationCts;
    private CancellationTokenSource? _pageCancellation;
    private int _bookLoadVersion;
    private int _chapterLoadVersion;
    private int _chapterCatalogVersion;
    private int _cacheRefreshGeneration;
    private int _cacheRefreshVersion;
    private int _chapterDecorationRequestVersion;
    private bool _isPageActive;
    private bool _isInvalidationRegistered;
    private bool _isExportEventsRegistered;
    private bool _isCacheRefreshRunning;
    private bool _cacheRefreshPending;
    private bool _cacheRefreshWholeBook;
    private bool _cacheRefreshReloadBooks;
    private bool _chapterCatalogProjectionPending;
    private Task? _chapterProjectionTask;
    private string? _cacheRefreshBookId;
    private string? _selectedBookId;
    private string? _selectedBookDecoration;

    public CacheManagementViewModel(
        IAudioCacheStore cacheStore,
        ICacheCatalog cacheCatalog,
        ICacheCoverageQuery cacheCoverageQuery,
        ICacheInvalidationCoordinator invalidationCoordinator,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        IAppNavigator navigator,
        IChapterExportCoordinator chapterExportCoordinator,
        IPresentationFileDialogService fileDialogs,
        IUiScheduler? uiScheduler = null,
        ICachePlanRepairRequestor? cachePlanRepairRequestor = null)
    {
        _cacheStore = cacheStore;
        _cacheCatalog = cacheCatalog;
        _cacheCoverageQuery = cacheCoverageQuery;
        _cachePlanRepairRequestor = cachePlanRepairRequestor;
        _invalidationCoordinator = invalidationCoordinator;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _navigator = navigator;
        _chapterExportCoordinator = chapterExportCoordinator;
        _fileDialogs = fileDialogs;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        _chapterSelection.SelectionChanged += OnChapterSelectionChanged;
    }

    public ObservableCollection<CachedBookListItemViewModel> Books => _books;

    public ObservableCollection<CachedChapterListItemViewModel> Chapters => _chapters;

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

    internal void RequestChapterDecorationWindow(int start, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var cancellationToken = _pageCancellation?.Token ?? new CancellationToken(canceled: true);
        void Request()
        {
            if (!_isPageActive ||
                _pageCancellation is not { IsCancellationRequested: false } ||
                string.IsNullOrWhiteSpace(_selectedBookId) ||
                _chapterCatalog.Count == 0)
            {
                return;
            }

            var chapterIndices = _chapterCatalog
                .Slice(Math.Clamp(start, 0, _chapterCatalog.Count - 1), count)
                .Select(static chapter => chapter.ChapterIndex)
                .ToArray();
            RequestChapterDecorationIndicesOnUi(chapterIndices, cancellationToken);
        }

        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(Request, cancellationToken),
                exception => ReportChapterDecorationRefreshFailure(exception, cancellationToken));
            return;
        }

        Request();
    }

    private void RequestChapterDecorationIndicesOnUi(
        IEnumerable<int> requestedChapterIndices,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        if (!_isPageActive ||
            _pageCancellation is not { IsCancellationRequested: false } ||
            string.IsNullOrWhiteSpace(_selectedBookId) ||
            _chapterCatalog.Count == 0)
        {
            return;
        }

        var chapterIndices = requestedChapterIndices
            .Where(chapterIndex => _chapterCatalog.TryGetPosition(chapterIndex, out _))
            .Distinct()
            .ToArray();
        if (chapterIndices.Length == 0 ||
            (!forceRefresh && _chapterDecorationWindow.SetEquals(chapterIndices)))
        {
            return;
        }

        var bookId = _selectedBookId;
        _chapterDecorationWindow.Clear();
        _chapterDecorationWindow.UnionWith(chapterIndices);
        ClearStaleChapterDecorations(chapterIndices);
        var requestVersion = Interlocked.Increment(ref _chapterDecorationRequestVersion);
        var refreshGeneration = _cacheRefreshGeneration;
        var refreshVersion = Volatile.Read(ref _cacheRefreshVersion);
        var decorationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previousDecorationCts = _chapterDecorationCts;
        _chapterDecorationCts = decorationCts;
        previousDecorationCts?.Cancel();
        _pageTasks.Register(
            RefreshChapterDecorationsOwnedAsync(
                bookId,
                chapterIndices,
                refreshGeneration,
                refreshVersion,
                requestVersion,
                decorationCts),
            exception => ReportChapterDecorationRefreshFailure(exception, cancellationToken));
    }

    public string ChapterSelectionSummary => $"已选择 {_chapterSelection.Count} 章";

    public bool CanClearSelectedChapters =>
        !IsBusy &&
        HasSelection &&
        !string.IsNullOrWhiteSpace(_selectedBookId) &&
        _chapterSelection.Count > 0;

    public bool CanExportSelectedChapters =>
        !IsBusy &&
        !_chapterCatalogProjectionPending &&
        !_chapters.IsProjectionPending &&
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

            if (_chapterCatalogProjectionPending || _chapters.IsProjectionPending)
            {
                return "正在更新章节列表";
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
        _bookProjectionCts?.Cancel();
        _bookProjectionCts = null;
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
        await _navigator.NavigateBackAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SelectBookAsync(CachedBookListItemViewModel? item, CancellationToken cancellationToken)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        _chapterSelection.Clear();
        ResetSelectedBookRefreshState(item.BookId);
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

    private void ResetSelectedBookRefreshState(string bookId)
    {
        lock (_cacheRefreshSync)
        {
            if (string.Equals(_cacheRefreshBookId, bookId, StringComparison.Ordinal) &&
                string.Equals(_selectedBookId, bookId, StringComparison.Ordinal))
            {
                return;
            }

            _cacheRefreshVersion++;
            _cacheRefreshBookId = bookId;
            _cacheRefreshWholeBook = false;
            _pendingCacheRefreshChapterIndices.Clear();
            _pendingCacheRefreshCoverageIndices.Clear();
        }
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

    public bool TryHandleEscape() => HandleClearChapterSelection();

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
            ct => _cacheStore.ClearChaptersAsync(selectedBookId, selectedIndices, ct),
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
        var selectedChapters = SelectedChapterIndices
            .Select(index => _chapterCatalog.TryGetPosition(index, out var position)
                ? _chapters[position]
                : null)
            .Where(static chapter => chapter is not null)
            .Cast<CachedChapterListItemViewModel>()
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
        Func<CancellationToken, Task<AudioCacheStoreCleanupResult>> cleanupAsync,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        NotifyCommandStateChanged();

        try
        {
            var result = await cleanupAsync(cancellationToken);
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

    private async Task LoadBooksAsync(
        CancellationToken cancellationToken,
        int? expectedGeneration = null,
        int? expectedRefreshVersion = null)
    {
        var version = Interlocked.Increment(ref _bookLoadVersion);
        var projectionCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _pageCancellation?.Token ?? CancellationToken.None);
        var previousProjectionCts = _bookProjectionCts;
        _bookProjectionCts = projectionCts;
        previousProjectionCts?.Cancel();
        IsLoadingBooks = true;
        try
        {
            var books = await _cacheCatalog.GetCachedBooksAsync(projectionCts.Token);
            projectionCts.Token.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _bookLoadVersion) ||
                (expectedGeneration is int loadGeneration &&
                 expectedRefreshVersion is int loadRefreshVersion &&
                 !IsCurrentCacheRefreshVersion(loadGeneration, loadRefreshVersion)))
            {
                return;
            }

            var selectedBookId = _selectedBookId;
            Func<bool> projectionIsCurrent = () =>
                version == Volatile.Read(ref _bookLoadVersion) &&
                (expectedGeneration is not int projectionGeneration ||
                 expectedRefreshVersion is not int projectionRefreshVersion ||
                 IsCurrentCacheRefreshVersion(projectionGeneration, projectionRefreshVersion));
            var bookPositions = books.Count < 512
                ? books
                    .Select((book, index) => (book.BookId, index))
                    .ToDictionary(item => item.BookId, item => item.index, StringComparer.Ordinal)
                : await Task.Run(
                    () => books
                        .Select((book, index) => (book.BookId, index))
                        .ToDictionary(item => item.BookId, item => item.index, StringComparer.Ordinal),
                    projectionCts.Token);

            await _books.ReplaceWithInBatchesAsync(
                books,
                book => CreateBookItem(
                    book,
                string.Equals(book.BookId, selectedBookId, StringComparison.Ordinal)),
                _uiScheduler,
                projectionCts.Token,
                preservePreviousItemsOnCancel: true,
                isCurrent: projectionIsCurrent,
                beforeComplete: () =>
                {
                    if (!projectionIsCurrent())
                    {
                        throw new OperationCanceledException(projectionCts.Token);
                    }

                    _bookPositions = bookPositions;
                    if (!string.IsNullOrWhiteSpace(selectedBookId) &&
                        !_bookPositions.ContainsKey(selectedBookId))
                    {
                        ClearSelection();
                    }
                    else
                    {
                        _selectedBookDecoration = selectedBookId;
                        UpdateBookSelection(_selectedBookId);
                    }
                });

            NotifyVisibilityStateChanged();
            NotifyCommandStateChanged();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref _bookLoadVersion) && _isPageActive)
            {
                _feedbackService.ShowProjectedNotification("加载缓存书籍失败", _feedbackService.Project(exception));
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _bookLoadVersion))
            {
                IsLoadingBooks = false;
            }

            if (ReferenceEquals(_bookProjectionCts, projectionCts))
            {
                _bookProjectionCts = null;
            }

            projectionCts.Dispose();
        }
    }

    private async Task LoadChaptersAsync(string bookId, CancellationToken cancellationToken)
    {
        var catalogVersion = Interlocked.Increment(ref _chapterCatalogVersion);
        int refreshGeneration;
        lock (_cacheRefreshSync)
        {
            refreshGeneration = _cacheRefreshGeneration;
        }

        CancelChapterLoad();
        _chapterLoadCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _pageCancellation?.Token ?? CancellationToken.None);
        var localCts = _chapterLoadCts;
        var version = Interlocked.Increment(ref _chapterLoadVersion);
        InvalidateChapterDecorationRequests();
        Func<bool> projectionIsCurrent = () =>
            IsCurrentChapterCatalog(refreshGeneration, catalogVersion, bookId, version);

        IsLoadingChapters = true;
        _chapterCatalog = new IndexedCatalog<CachedChapterCatalogItem>(
            [],
            static chapter => chapter.ChapterIndex);
        _chapterSelectionDecorations.Clear();
        _chapterRefreshOverrides.Clear();
        _pendingChapterRowRefreshes.Clear();
        _chapterCatalogProjectionPending = false;
        Chapters.Clear();
        _chapterSelection.SetItems([]);
        NotifyVisibilityStateChanged();

        try
        {
            var chapters = await _cacheCatalog.GetCachedChapterCatalogAsync(bookId, localCts.Token);
            if (version != Volatile.Read(ref _chapterLoadVersion) ||
                !projectionIsCurrent())
            {
                return;
            }

            var projection = chapters.Count < 512
                ? CreateChapterProjection(chapters)
                : await Task.Run(
                    () => CreateChapterProjection(chapters),
                    localCts.Token);
            localCts.Token.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _chapterLoadVersion) ||
                !projectionIsCurrent())
            {
                return;
            }

            var catalog = projection;
            _chapterSelectionDecorations.Clear();
            _chapterRefreshOverrides.Clear();
            var selectionItems = catalog.Keys;
            var selectionPositions = catalog.Positions;
            var selectionSnapshot = _chapterSelectionDecorations.Snapshot();
            var decorationSnapshot = _chapterRefreshOverrides.Snapshot();
            _chapterCatalogProjectionPending = true;
            NotifyCommandStateChanged();
            var projectionTask = _chapters.ReplaceWithInBatchesAsync(
                catalog.Items,
                chapter => CreateChapterItem(
                    chapter,
                    selectionSnapshot,
                    decorationSnapshot),
                _uiScheduler,
                localCts.Token,
                preservePreviousItemsOnCancel: true,
                isCurrent: projectionIsCurrent,
                beforeComplete: () =>
                {
                    if (!projectionIsCurrent())
                    {
                        throw new OperationCanceledException(localCts.Token);
                    }

                    _chapterCatalog = catalog;
                    _chapterSelection.SetIndexedItems(
                        selectionItems,
                        selectionPositions,
                        resetSelection: true);
                    _chapterCatalogProjectionPending = false;
                    ReplayPendingChapterRows(_chapters.IsProjectionPending);
                    NotifyCommandStateChanged();
                });
            _chapterProjectionTask = projectionTask;
            try
            {
                await projectionTask;
            }
            finally
            {
                if (ReferenceEquals(_chapterProjectionTask, projectionTask))
                {
                    _chapterProjectionTask = null;
                }

                _chapterCatalogProjectionPending = false;
                NotifyCommandStateChanged();
            }

            if (version != Volatile.Read(ref _chapterLoadVersion) ||
                !projectionIsCurrent())
            {
                return;
            }

            SelectedBookHasCache = Chapters.Count > 0;
            NotifyVisibilityStateChanged();
            RequestChapterDecorationWindow(0, ChapterDecorationWindowSize);
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

    private static IndexedCatalog<CachedChapterCatalogItem> CreateChapterProjection(
        IReadOnlyList<CachedChapterCatalogEntry> chapters)
    {
        return new IndexedCatalog<CachedChapterCatalogItem>(
            chapters.Select(CachedChapterCatalogItem.From).ToArray(),
            static chapter => chapter.ChapterIndex);
    }

    private async Task RefreshChapterDecorationsAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        int refreshGeneration,
        int refreshVersion,
        int requestVersion,
        CancellationToken cancellationToken)
    {
        if (chapterIndices.Count == 0)
        {
            return;
        }

        await _chapterDecorationQueryGate.WaitAsync(cancellationToken);
        try
        {
            var chapterLoadVersion = Volatile.Read(ref _chapterLoadVersion);
            var physicalChaptersTask = _cacheCatalog.GetCachedChaptersAsync(
                bookId,
                chapterIndices,
                cancellationToken);
            var coverageStatusesTask = _cacheCoverageQuery.GetAsync(
                bookId,
                chapterIndices,
                cancellationToken);
            await Task.WhenAll(physicalChaptersTask, coverageStatusesTask);
            cancellationToken.ThrowIfCancellationRequested();
            var coverageStatuses = coverageStatusesTask.Result;
            if (_cachePlanRepairRequestor is not null &&
                coverageStatuses.Any(static status => status.Kind is
                    ChapterCacheStatusKind.PlanMissing or ChapterCacheStatusKind.PlanStale))
            {
                await _cachePlanRepairRequestor.RequestAsync(
                    bookId,
                    chapterIndices,
                    coverageStatuses,
                    cancellationToken);
            }

            if (!IsCurrentCacheRefresh(
                    refreshGeneration,
                    refreshVersion,
                    bookId,
                    chapterLoadVersion) ||
                requestVersion != Volatile.Read(ref _chapterDecorationRequestVersion) ||
                !string.Equals(bookId, _selectedBookId, StringComparison.Ordinal))
            {
                return;
            }

            var physicalByIndex = physicalChaptersTask.Result.ToDictionary(static chapter => chapter.ChapterIndex);
            var statusByIndex = coverageStatuses.ToDictionary(static status => status.ChapterIndex);
            foreach (var chapterIndex in chapterIndices)
            {
                if (!_chapterCatalog.TryGetPosition(chapterIndex, out var position))
                {
                    continue;
                }

                var decoration = _chapterRefreshOverrides.TryGet(chapterIndex, out var currentDecoration)
                    ? currentDecoration
                    : CachedChapterDecoration.Placeholder(_chapterCatalog[position].Title);
                if (physicalByIndex.TryGetValue(chapterIndex, out var physicalChapter))
                {
                    decoration = decoration with
                    {
                        Title = physicalChapter.Title,
                        EntryCount = physicalChapter.EntryCount,
                        TotalSizeBytes = physicalChapter.TotalSizeBytes
                    };
                }

                if (statusByIndex.TryGetValue(chapterIndex, out var status))
                {
                    decoration = decoration with
                    {
                        CachedSegmentCount = status.CachedSegmentCount,
                        CurrentConfigurationSegmentCount = status.TotalSegmentCount,
                        CurrentConfigurationStatus = status.Kind
                    };
                }

                _chapterRefreshOverrides.Set(chapterIndex, decoration);
                RefreshChapterRow(chapterIndex);
            }
        }
        finally
        {
            _chapterDecorationQueryGate.Release();
        }
    }

    private async Task RefreshChapterDecorationsOwnedAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        int refreshGeneration,
        int refreshVersion,
        int requestVersion,
        CancellationTokenSource decorationCts)
    {
        try
        {
            await RefreshChapterDecorationsAsync(
                bookId,
                chapterIndices,
                refreshGeneration,
                refreshVersion,
                requestVersion,
                decorationCts.Token);
        }
        finally
        {
            if (ReferenceEquals(_chapterDecorationCts, decorationCts))
            {
                _chapterDecorationCts = null;
            }

            decorationCts.Dispose();
        }
    }

    private void RefreshChapterRow(int chapterIndex)
    {
        if (!_chapterCatalog.TryGetPosition(chapterIndex, out var position))
        {
            return;
        }

        if (_chapterCatalogProjectionPending || _chapters.IsProjectionPending)
        {
            _pendingChapterRowRefreshes.Add(chapterIndex);
            return;
        }

        if (position < _chapters.Count)
        {
            _chapters.ReplaceAt(position, CreateChapterItem(_chapterCatalog[position]));
        }
    }

    private void ClearStaleChapterDecorations(IReadOnlyCollection<int> requestedChapterIndices)
    {
        var requested = requestedChapterIndices.ToHashSet();
        foreach (var chapterIndex in _chapterRefreshOverrides.Snapshot().Keys)
        {
            if (requested.Contains(chapterIndex))
            {
                continue;
            }

            _chapterRefreshOverrides.Remove(chapterIndex);
            RefreshChapterRow(chapterIndex);
        }
    }

    private void ReportChapterDecorationRefreshFailure(
        Exception exception,
        CancellationToken activationToken)
    {
        if (IsCurrentPageActivation(activationToken))
        {
            _feedbackService.ShowProjectedNotification(
                "刷新章节缓存状态失败",
                _feedbackService.Project(exception));
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
        _chapterCatalog = new IndexedCatalog<CachedChapterCatalogItem>(
            [],
            static chapter => chapter.ChapterIndex);
        Interlocked.Increment(ref _chapterCatalogVersion);
        InvalidateChapterDecorationRequests();
        _chapterSelectionDecorations.Clear();
        _chapterRefreshOverrides.Clear();
        _pendingChapterRowRefreshes.Clear();
        _chapterCatalogProjectionPending = false;
        Chapters.Clear();
        _chapterSelection.SetItems([]);
        UpdateBookSelection(null);
        NotifyVisibilityStateChanged();
    }

    private void UpdateBookSelection(string? selectedBookId)
    {
        var previousBookId = _selectedBookDecoration;
        _selectedBookDecoration = selectedBookId;
        if (string.Equals(previousBookId, selectedBookId, StringComparison.Ordinal))
        {
            return;
        }

        ReplaceBookSelection(previousBookId, isSelected: false);
        ReplaceBookSelection(selectedBookId, isSelected: true);
    }

    private void ReplaceBookSelection(string? bookId, bool isSelected)
    {
        if (string.IsNullOrWhiteSpace(bookId) ||
            !_bookPositions.TryGetValue(bookId, out var position))
        {
            return;
        }

        _books.ReplaceAt(position, _books[position].WithSelection(isSelected));
    }

    private void RebuildBookPositions()
    {
        _bookPositions = Books
            .Select((book, index) => (book.BookId, index))
            .ToDictionary(item => item.BookId, item => item.index, StringComparer.Ordinal);
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
        Interlocked.Increment(ref _chapterCatalogVersion);
        lock (_cacheRefreshSync)
        {
            _isPageActive = true;
            _cacheRefreshGeneration++;
            _cacheRefreshVersion++;
            _cacheRefreshPending = false;
            _cacheRefreshWholeBook = false;
            _cacheRefreshReloadBooks = false;
            _pendingCacheRefreshChapterIndices.Clear();
            _pendingCacheRefreshCoverageIndices.Clear();
            _pendingCacheRefreshBookIds.Clear();
            _cacheRefreshBookEpochs.Clear();
            _cacheRefreshBookId = null;
            _isCacheRefreshRunning = false;
        }

        _invalidationCoordinator.BatchPublished += OnInvalidationBatchPublished;
        _isInvalidationRegistered = true;
        _chapterExportCoordinator.SnapshotChanged += OnChapterExportSnapshotChanged;
        _isExportEventsRegistered = true;
    }

    private void DeactivatePage()
    {
        if (_isInvalidationRegistered)
        {
            _invalidationCoordinator.BatchPublished -= OnInvalidationBatchPublished;
            _isInvalidationRegistered = false;
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
            _cacheRefreshVersion++;
            _cacheRefreshPending = false;
            _cacheRefreshWholeBook = false;
            _cacheRefreshReloadBooks = false;
            _pendingCacheRefreshChapterIndices.Clear();
            _pendingCacheRefreshCoverageIndices.Clear();
            _pendingCacheRefreshBookIds.Clear();
            _cacheRefreshBookEpochs.Clear();
            _cacheRefreshBookId = null;
            _isCacheRefreshRunning = false;
        }

        Interlocked.Increment(ref _chapterCatalogVersion);
        InvalidateChapterDecorationRequests();
        _pendingChapterRowRefreshes.Clear();
        _chapterCatalogProjectionPending = false;
        pageCancellation?.Cancel();
        pageCancellation?.Dispose();
    }

    private void OnInvalidationBatchPublished(object? sender, CacheInvalidationBatch batch)
    {
        if (!_isInvalidationRegistered || !TryGetActivePageCancellationToken(out var cancellationToken))
        {
            return;
        }

        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(
                    () => QueueCacheRefresh(batch),
                    cancellationToken),
                exception => ReportCacheRefreshFailure(exception, cancellationToken));
            return;
        }

        QueueCacheRefresh(batch);
    }

    private void QueueCacheRefresh(CacheInvalidationBatch batch)
    {
        int generation;
        CancellationToken cancellationToken;
        lock (_cacheRefreshSync)
        {
            if (!_isPageActive ||
                _pageCancellation is not { IsCancellationRequested: false } pageCancellation)
            {
                return;
            }

            var queued = false;
            var selectedProjectionDirty = false;
            foreach (var change in batch.Changes)
            {
                var result = QueueCacheRefresh(change);
                queued |= result.Queued;
                selectedProjectionDirty |= result.SelectedProjectionDirty;
            }

            if (!queued)
            {
                return;
            }

            if (selectedProjectionDirty)
            {
                _cacheRefreshVersion++;
            }
            _cacheRefreshPending = true;
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
            exception => ReportCacheRefreshFailure(exception, cancellationToken));
    }

    private (bool Queued, bool SelectedProjectionDirty) QueueCacheRefresh(
        CacheInvalidation change)
    {
        var aspects = change.Aspects;
        var queued = false;
        var selectedProjectionDirty = false;
        switch (change.Scope)
        {
            case CacheInvalidationScope.Global:
                if (aspects.HasFlag(CacheInvalidationAspect.PhysicalSummary) ||
                    aspects.HasFlag(CacheInvalidationAspect.CatalogStructure))
                {
                    _cacheRefreshReloadBooks = true;
                    queued = true;
                }

                selectedProjectionDirty = !string.IsNullOrWhiteSpace(_selectedBookId) &&
                    (aspects.HasFlag(CacheInvalidationAspect.PhysicalSummary) ||
                     aspects.HasFlag(CacheInvalidationAspect.CatalogStructure) ||
                     aspects.HasFlag(CacheInvalidationAspect.Coverage));

                if (aspects.HasFlag(CacheInvalidationAspect.CatalogStructure))
                {
                    _cacheRefreshWholeBook = true;
                    InvalidateChapterCatalogProjection();
                }

                if (aspects.HasFlag(CacheInvalidationAspect.PhysicalSummary) ||
                    aspects.HasFlag(CacheInvalidationAspect.Coverage))
                {
                    if (aspects.HasFlag(CacheInvalidationAspect.PhysicalSummary))
                    {
                        queued |= QueueSelectedChapterWindow(_pendingCacheRefreshChapterIndices);
                    }

                    if (aspects.HasFlag(CacheInvalidationAspect.Coverage))
                    {
                        queued |= QueueSelectedChapterWindow(
                            _pendingCacheRefreshCoverageIndices,
                            forceDirty: true);
                    }
                }

                break;
            case CacheInvalidationScope.Book book:
                if (aspects.HasFlag(CacheInvalidationAspect.PhysicalSummary) ||
                    aspects.HasFlag(CacheInvalidationAspect.CatalogStructure))
                {
                    TouchBookRefreshEpoch(book.BookId);
                    _pendingCacheRefreshBookIds.Add(book.BookId);
                    queued = true;
                }

                if (string.Equals(book.BookId, _selectedBookId, StringComparison.Ordinal))
                {
                    selectedProjectionDirty = aspects.HasFlag(CacheInvalidationAspect.PhysicalSummary) ||
                                               aspects.HasFlag(CacheInvalidationAspect.CatalogStructure) ||
                                               aspects.HasFlag(CacheInvalidationAspect.Coverage);
                    if (aspects.HasFlag(CacheInvalidationAspect.CatalogStructure))
                    {
                        _cacheRefreshWholeBook = true;
                        InvalidateChapterCatalogProjection();
                    }

                    if (aspects.HasFlag(CacheInvalidationAspect.PhysicalSummary))
                    {
                        queued |= QueueSelectedChapterWindow(_pendingCacheRefreshChapterIndices);
                    }

                    if (aspects.HasFlag(CacheInvalidationAspect.Coverage))
                    {
                        queued |= QueueSelectedChapterWindow(
                            _pendingCacheRefreshCoverageIndices,
                            forceDirty: true);
                    }
                }

                break;
            case CacheInvalidationScope.Chapters chapters:
                if (aspects.HasFlag(CacheInvalidationAspect.PhysicalSummary) ||
                    aspects.HasFlag(CacheInvalidationAspect.CatalogStructure))
                {
                    TouchBookRefreshEpoch(chapters.BookId);
                    _pendingCacheRefreshBookIds.Add(chapters.BookId);
                    queued = true;

                    if (string.Equals(chapters.BookId, _selectedBookId, StringComparison.Ordinal))
                    {
                        var pendingCount = _pendingCacheRefreshChapterIndices.Count;
                        _pendingCacheRefreshChapterIndices.UnionWith(chapters.ChapterIndices);
                        queued |= _pendingCacheRefreshChapterIndices.Count > pendingCount;
                    }
                }

                if (string.Equals(chapters.BookId, _selectedBookId, StringComparison.Ordinal))
                {
                    selectedProjectionDirty = aspects.HasFlag(CacheInvalidationAspect.PhysicalSummary) ||
                                               aspects.HasFlag(CacheInvalidationAspect.CatalogStructure) ||
                                               aspects.HasFlag(CacheInvalidationAspect.Coverage);
                    if (aspects.HasFlag(CacheInvalidationAspect.CatalogStructure))
                    {
                        _cacheRefreshWholeBook = true;
                        InvalidateChapterCatalogProjection();
                    }
                    if (aspects.HasFlag(CacheInvalidationAspect.Coverage))
                    {
                        var coverageChapterIndices = chapters.ChapterIndices
                            .Where(chapterIndex => _chapterCatalog.TryGetPosition(chapterIndex, out _))
                            .ToArray();
                        if (coverageChapterIndices.Length > 0)
                        {
                            _pendingCacheRefreshCoverageIndices.UnionWith(coverageChapterIndices);
                            queued = true;
                        }
                    }
                }

                break;
        }

        if (queued)
        {
            _cacheRefreshBookId = _selectedBookId;
        }

        return (queued, selectedProjectionDirty);
    }

    private void TouchBookRefreshEpoch(string bookId)
    {
        _cacheRefreshBookEpochs[bookId] =
            _cacheRefreshBookEpochs.GetValueOrDefault(bookId) + 1;
    }

    private bool QueueSelectedChapterWindow(
        HashSet<int> pendingChapterIndices,
        bool forceDirty = false)
    {
        if (string.IsNullOrWhiteSpace(_selectedBookId) || _chapterCatalog.Count == 0)
        {
            return false;
        }

        var window = _chapterDecorationWindow.Count > 0
            ? _chapterDecorationWindow
                : _chapterCatalog
                .Slice(0, Math.Min(ChapterDecorationWindowSize, _chapterCatalog.Count))
                .Select(static chapter => chapter.ChapterIndex);
        var pendingCount = pendingChapterIndices.Count;
        pendingChapterIndices.UnionWith(window);
        return forceDirty || pendingChapterIndices.Count > pendingCount;
    }

    private void InvalidateChapterDecorationRequests()
    {
        Interlocked.Increment(ref _chapterDecorationRequestVersion);
        _chapterDecorationWindow.Clear();
        _chapterDecorationCts?.Cancel();
    }

    private void InvalidateChapterCatalogProjection()
    {
        Interlocked.Increment(ref _chapterCatalogVersion);
        InvalidateChapterDecorationRequests();
    }

    private async Task RefreshChangedChaptersAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                string[] bookIds;
                string? selectedBookId;
                bool reloadBooks;
                bool refreshWholeBook;
                int[] physicalChapterIndices;
                int[] coverageChapterIndices;
                int refreshVersion;
                Dictionary<string, int> bookEpochs;
                lock (_cacheRefreshSync)
                {
                    if (generation != _cacheRefreshGeneration || !_isPageActive)
                    {
                        return;
                    }

                    if (!_cacheRefreshPending)
                    {
                        _isCacheRefreshRunning = false;
                        return;
                    }

                    _cacheRefreshPending = false;
                    refreshVersion = _cacheRefreshVersion;
                    selectedBookId = _cacheRefreshBookId;
                    reloadBooks = _cacheRefreshReloadBooks;
                    refreshWholeBook = _cacheRefreshWholeBook;
                    physicalChapterIndices = _pendingCacheRefreshChapterIndices.ToArray();
                    coverageChapterIndices = _pendingCacheRefreshCoverageIndices.ToArray();
                    bookIds = _pendingCacheRefreshBookIds.ToArray();
                    bookEpochs = bookIds.ToDictionary(
                        bookId => bookId,
                        bookId => _cacheRefreshBookEpochs.GetValueOrDefault(bookId),
                        StringComparer.Ordinal);
                    _cacheRefreshReloadBooks = false;
                    _cacheRefreshWholeBook = false;
                    _pendingCacheRefreshChapterIndices.Clear();
                    _pendingCacheRefreshCoverageIndices.Clear();
                    _pendingCacheRefreshBookIds.Clear();
                }

                if (reloadBooks)
                {
                    await LoadBooksAsync(cancellationToken, generation, refreshVersion);
                }

                if (bookIds.Length > 0)
                {
                    await RefreshBookSummariesAsync(
                        bookIds,
                        bookEpochs,
                        generation,
                        refreshVersion,
                        cancellationToken);
                }

                if (refreshWholeBook &&
                    !string.IsNullOrWhiteSpace(selectedBookId) &&
                    string.Equals(selectedBookId, _selectedBookId, StringComparison.Ordinal))
                {
                    await RefreshChapterCatalogAsync(
                        selectedBookId,
                        generation,
                        refreshVersion,
                        coverageChapterIndices,
                        cancellationToken);
                }

                var selectedBookIsCurrent = !string.IsNullOrWhiteSpace(selectedBookId) &&
                                            string.Equals(selectedBookId, _selectedBookId, StringComparison.Ordinal);
                if (selectedBookIsCurrent && !refreshWholeBook)
                {
                    if (physicalChapterIndices.Length > 0)
                    {
                        await RefreshChaptersAsync(
                            selectedBookId!,
                            physicalChapterIndices,
                            generation,
                            refreshVersion,
                            cancellationToken);
                    }

                    if (coverageChapterIndices.Length > 0)
                    {
                        await RefreshChapterDecorationsAsync(
                            selectedBookId!,
                            coverageChapterIndices,
                            generation,
                            refreshVersion,
                            Volatile.Read(ref _chapterDecorationRequestVersion),
                            cancellationToken);
                    }
                }

                var selectedRefreshIsStale = !IsCurrentCacheRefreshVersion(generation, refreshVersion);
                var bookRefreshIsStale = !AreCurrentBookRefreshEpochs(bookEpochs);
                if (selectedRefreshIsStale || bookRefreshIsStale)
                {
                    lock (_cacheRefreshSync)
                    {
                        if (generation == _cacheRefreshGeneration && _isPageActive)
                        {
                            _cacheRefreshReloadBooks |= reloadBooks;
                            if (selectedRefreshIsStale && selectedBookIsCurrent)
                            {
                                _cacheRefreshWholeBook |= refreshWholeBook;
                                _pendingCacheRefreshChapterIndices.UnionWith(physicalChapterIndices);
                                _pendingCacheRefreshCoverageIndices.UnionWith(coverageChapterIndices);
                            }
                            _pendingCacheRefreshBookIds.UnionWith(bookIds);
                            _cacheRefreshBookId = _selectedBookId;
                            _cacheRefreshPending = true;
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            var restartPending = false;
            lock (_cacheRefreshSync)
            {
                if (generation == _cacheRefreshGeneration)
                {
                    _isCacheRefreshRunning = false;
                    restartPending = _cacheRefreshPending &&
                                     _isPageActive &&
                                     _pageCancellation is { IsCancellationRequested: false };
                    if (restartPending)
                    {
                        _isCacheRefreshRunning = true;
                    }
                }
            }

            if (restartPending)
            {
                _pageTasks.Register(
                    RefreshChangedChaptersAsync(generation, cancellationToken),
                    exception => ReportCacheRefreshFailure(exception, cancellationToken));
            }
        }
    }

    private async Task RefreshChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        int generation,
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        if (chapterIndices.Count == 0)
        {
            return;
        }

        await _chapterDecorationQueryGate.WaitAsync(cancellationToken);
        try
        {
            var chapterLoadVersion = Volatile.Read(ref _chapterLoadVersion);
            var chapters = await _cacheCatalog.GetCachedChaptersAsync(
                bookId,
                chapterIndices,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentCacheRefresh(generation, refreshVersion, bookId, chapterLoadVersion))
            {
                return;
            }

            foreach (var chapter in chapters)
            {
                if (!_chapterCatalog.TryGetPosition(chapter.ChapterIndex, out _))
                {
                    continue;
                }

                var decoration = _chapterRefreshOverrides.TryGet(chapter.ChapterIndex, out var currentDecoration)
                    ? currentDecoration
                    : CachedChapterDecoration.Placeholder(chapter.Title);
                _chapterRefreshOverrides.Set(
                    chapter.ChapterIndex,
                    decoration with
                    {
                        Title = chapter.Title,
                        EntryCount = chapter.EntryCount,
                        TotalSizeBytes = chapter.TotalSizeBytes
                    });
                RefreshChapterRow(chapter.ChapterIndex);
            }

            SelectedBookHasCache = _chapterCatalog.Count > 0;
            NotifyVisibilityStateChanged();
            NotifyCommandStateChanged();
        }
        finally
        {
            _chapterDecorationQueryGate.Release();
        }
    }

    private async Task RefreshChapterCatalogAsync(
        string bookId,
        int generation,
        int refreshVersion,
        IReadOnlyCollection<int> targetedDecorationIndices,
        CancellationToken cancellationToken)
    {
        var existingProjectionTask = _chapterProjectionTask;
        if (existingProjectionTask is not null)
        {
            try
            {
                await existingProjectionTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        var chapterLoadVersion = Volatile.Read(ref _chapterLoadVersion);
        var catalogVersion = Volatile.Read(ref _chapterCatalogVersion);
        var chapters = await _cacheCatalog
            .GetCachedChapterCatalogAsync(bookId, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentChapterCatalog(generation, catalogVersion, bookId, chapterLoadVersion))
        {
            return;
        }

        var catalog = chapters.Count < 512
            ? CreateChapterProjection(chapters)
            : await Task.Run(
                () => CreateChapterProjection(chapters),
                cancellationToken).ConfigureAwait(false);

        IndexedCatalog<CachedChapterCatalogItem>? previousCatalog = null;
        await _uiScheduler.InvokeAsync(
            () => previousCatalog = _chapterCatalog,
            cancellationToken).ConfigureAwait(false);
        if (previousCatalog is null ||
            !IsCurrentChapterCatalog(generation, catalogVersion, bookId, chapterLoadVersion))
        {
            return;
        }

        var delta = previousCatalog.Count + catalog.Count < 512
            ? CreateChapterCatalogDelta(previousCatalog, catalog)
            : await Task.Run(
                () => CreateChapterCatalogDelta(previousCatalog, catalog),
                cancellationToken).ConfigureAwait(false);

        await _uiScheduler.InvokeAsync(
            async () =>
            {
                if (!IsCurrentChapterCatalog(generation, catalogVersion, bookId, chapterLoadVersion))
                {
                    return;
                }

                await ApplyChapterCatalogDeltaAsync(
                    delta,
                    generation,
                    catalogVersion,
                    bookId,
                    chapterLoadVersion,
                    cancellationToken);
                if (!IsCurrentChapterCatalog(generation, catalogVersion, bookId, chapterLoadVersion))
                {
                    return;
                }

                SelectedBookHasCache = catalog.Count > 0;
                NotifyVisibilityStateChanged();
                NotifyCommandStateChanged();
                var visibleChapterIndices = _chapterDecorationWindow.Count > 0
                    ? _chapterDecorationWindow
                    : _chapterCatalog
                        .Slice(0, Math.Min(ChapterDecorationWindowSize, _chapterCatalog.Count))
                        .Select(static chapter => chapter.ChapterIndex);
                RequestChapterDecorationIndicesOnUi(
                    visibleChapterIndices.Concat(targetedDecorationIndices),
                    cancellationToken,
                    forceRefresh: targetedDecorationIndices.Count > 0);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static ChapterCatalogDelta CreateChapterCatalogDelta(
        IndexedCatalog<CachedChapterCatalogItem> previousCatalog,
        IndexedCatalog<CachedChapterCatalogItem> catalog)
    {
        var removedKeys = previousCatalog.Keys
            .Where(chapterIndex => !catalog.TryGetPosition(chapterIndex, out _))
            .ToArray();
        var removedPositions = removedKeys
            .Select(chapterIndex => previousCatalog.Positions[chapterIndex])
            .OrderByDescending(static position => position)
            .ToArray();
        var added = new List<ChapterCatalogInsertion>();
        var replacements = new List<ChapterCatalogReplacement>();
        for (var position = 0; position < catalog.Count; position++)
        {
            var chapter = catalog[position];
            if (!previousCatalog.TryGetPosition(chapter.ChapterIndex, out _))
            {
                added.Add(new ChapterCatalogInsertion(position, chapter));
                continue;
            }

            if (!previousCatalog.TryGet(chapter.ChapterIndex, out var previous) ||
                !string.Equals(previous.Title, chapter.Title, StringComparison.Ordinal))
            {
                replacements.Add(new ChapterCatalogReplacement(position, chapter));
            }
        }

        return new ChapterCatalogDelta(
            catalog,
            removedKeys,
            removedPositions,
            added,
            replacements);
    }

    private async Task ApplyChapterCatalogDeltaAsync(
        ChapterCatalogDelta delta,
        int generation,
        int catalogVersion,
        string bookId,
        int chapterLoadVersion,
        CancellationToken cancellationToken)
    {
        if (delta.ChangeCount > 512)
        {
            var selectionSnapshot = _chapterSelectionDecorations.Snapshot();
            var decorationSnapshot = _chapterRefreshOverrides.Snapshot();
            _pendingChapterRowRefreshes.Clear();
            _chapterCatalogProjectionPending = true;
            NotifyCommandStateChanged();
            try
            {
                await _chapters.ReplaceWithInBatchesAsync(
                    delta.Catalog.Items,
                    chapter => CreateChapterItem(
                        chapter,
                        selectionSnapshot,
                        decorationSnapshot),
                    _uiScheduler,
                    cancellationToken,
                    notifyEachBatch: false,
                    preservePreviousItemsOnCancel: true,
                    isCurrent: () => IsCurrentChapterCatalog(
                        generation,
                        catalogVersion,
                        bookId,
                        chapterLoadVersion),
                    beforeComplete: () =>
                    {
                        if (!IsCurrentChapterCatalog(
                                generation,
                                catalogVersion,
                                bookId,
                                chapterLoadVersion))
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        _chapterCatalog = delta.Catalog;
                        RemoveChapterDecorations(delta.RemovedKeys);
                        _chapterSelection.UpdateIndexedItems(
                            delta.Catalog.Keys,
                            delta.Catalog.Positions);
                        _chapterCatalogProjectionPending = false;
                        ReplayPendingChapterRows(_chapters.IsProjectionPending);
                        NotifyCommandStateChanged();
                    });
            }
            catch
            {
                _pendingChapterRowRefreshes.Clear();
                throw;
            }
            finally
            {
                if (_chapterCatalogProjectionPending)
                {
                    _chapterCatalogProjectionPending = false;
                    NotifyCommandStateChanged();
                }
            }

            return;
        }

        foreach (var position in delta.RemovedPositions)
        {
            if ((uint)position < (uint)_chapters.Count)
            {
                _chapters.RemoveAt(position);
            }
        }

        foreach (var insertion in delta.Added)
        {
            var position = Math.Min(insertion.Position, _chapters.Count);
            _chapters.Insert(position, CreateChapterItem(insertion.Item));
        }

        foreach (var replacement in delta.Replacements)
        {
            if ((uint)replacement.Position < (uint)_chapters.Count)
            {
                _chapters.ReplaceAt(replacement.Position, CreateChapterItem(replacement.Item));
            }
        }

        _chapterCatalog = delta.Catalog;
        RemoveChapterDecorations(delta.RemovedKeys);
        _chapterSelection.UpdateIndexedItems(
            delta.Catalog.Keys,
            delta.Catalog.Positions);
    }

    private void RemoveChapterDecorations(IReadOnlyCollection<int> chapterIndices)
    {
        foreach (var chapterIndex in chapterIndices)
        {
            _chapterSelectionDecorations.Remove(chapterIndex);
            _chapterRefreshOverrides.Remove(chapterIndex);
        }
    }

    private void ReplayPendingChapterRows(bool collectionIsReplacing)
    {
        var chapterIndices = _pendingChapterRowRefreshes.ToArray();
        _pendingChapterRowRefreshes.Clear();
        foreach (var chapterIndex in chapterIndices)
        {
            if (!_chapterCatalog.TryGetPosition(chapterIndex, out var position) ||
                position >= _chapters.Count)
            {
                continue;
            }

            _chapters.ReplaceAt(
                position,
                CreateChapterItem(_chapterCatalog[position]),
                notify: !collectionIsReplacing);
        }
    }

    private sealed record ChapterCatalogDelta(
        IndexedCatalog<CachedChapterCatalogItem> Catalog,
        IReadOnlyList<int> RemovedKeys,
        IReadOnlyList<int> RemovedPositions,
        IReadOnlyList<ChapterCatalogInsertion> Added,
        IReadOnlyList<ChapterCatalogReplacement> Replacements)
    {
        public int ChangeCount => RemovedKeys.Count + Added.Count + Replacements.Count;
    }

    private sealed record ChapterCatalogInsertion(
        int Position,
        CachedChapterCatalogItem Item);

    private sealed record ChapterCatalogReplacement(
        int Position,
        CachedChapterCatalogItem Item);

    private bool IsCurrentCacheRefresh(
        int generation,
        int refreshVersion,
        string bookId,
        int chapterLoadVersion)
    {
        lock (_cacheRefreshSync)
        {
            return generation == _cacheRefreshGeneration &&
                   refreshVersion == _cacheRefreshVersion &&
                   _isPageActive &&
                   string.Equals(bookId, _selectedBookId, StringComparison.Ordinal) &&
                   chapterLoadVersion == Volatile.Read(ref _chapterLoadVersion);
        }
    }

    private bool IsCurrentChapterCatalog(
        int generation,
        int catalogVersion,
        string bookId,
        int chapterLoadVersion)
    {
        lock (_cacheRefreshSync)
        {
            return generation == _cacheRefreshGeneration &&
                   catalogVersion == Volatile.Read(ref _chapterCatalogVersion) &&
                   _isPageActive &&
                   string.Equals(bookId, _selectedBookId, StringComparison.Ordinal) &&
                   chapterLoadVersion == Volatile.Read(ref _chapterLoadVersion);
        }
    }

    private bool IsCurrentCacheRefreshVersion(int generation, int refreshVersion)
    {
        lock (_cacheRefreshSync)
        {
            return generation == _cacheRefreshGeneration &&
                   refreshVersion == _cacheRefreshVersion &&
                   _isPageActive &&
                   _pageCancellation is { IsCancellationRequested: false };
        }
    }

    private async Task RefreshBookSummariesAsync(
        IReadOnlyCollection<string> bookIds,
        IReadOnlyDictionary<string, int> bookEpochs,
        int generation,
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        var books = await _cacheCatalog.GetCachedBooksAsync(bookIds, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentCacheRefreshVersion(generation, refreshVersion) ||
            !AreCurrentBookRefreshEpochs(bookEpochs))
        {
            return;
        }

        var booksById = books.ToDictionary(static book => book.BookId, StringComparer.Ordinal);
        var requiresFullReload = false;
        foreach (var bookId in bookIds)
        {
            if (!booksById.TryGetValue(bookId, out var book))
            {
                if (_bookPositions.TryGetValue(bookId, out var removedPosition))
                {
                    _books.RemoveAt(removedPosition);
                    RebuildBookPositions();
                }

                if (string.Equals(_selectedBookId, bookId, StringComparison.Ordinal))
                {
                    ClearSelection();
                }

                continue;
            }

            if (!_bookPositions.TryGetValue(book.BookId, out var bookPosition))
            {
                requiresFullReload = true;
                continue;
            }

            var isSelectedBook = string.Equals(_selectedBookId, book.BookId, StringComparison.Ordinal);
            if (isSelectedBook)
            {
                SelectedBookTitle = book.Title;
                SelectedBookAuthor = book.Author ?? "未知作者";
                SelectedBookCacheSizeText = CacheCleanupFeedbackFormatter.FormatBytes(book.TotalSizeBytes);
                SelectedBookChapterCountText = $"已缓存 {book.ChapterCount} 章";
            }

            var updatedBook = CreateBookItem(
                book,
                isSelected: string.Equals(book.BookId, _selectedBookDecoration, StringComparison.Ordinal));
            var targetPosition = FindBookInsertionPosition(updatedBook, bookPosition);
            _books.ReplaceAt(bookPosition, updatedBook);
            if (targetPosition != bookPosition)
            {
                _books.Move(bookPosition, targetPosition);
                RebuildBookPositions();
            }
        }

        if (requiresFullReload)
        {
            await LoadBooksAsync(cancellationToken, generation, refreshVersion);
            if (!IsCurrentCacheRefreshVersion(generation, refreshVersion))
            {
                return;
            }
        }

        SelectedBookHasCache = _chapterCatalog.Count > 0;
        NotifyVisibilityStateChanged();
        NotifyCommandStateChanged();
    }

    private bool AreCurrentBookRefreshEpochs(IReadOnlyDictionary<string, int> bookEpochs)
    {
        lock (_cacheRefreshSync)
        {
            return bookEpochs.All(pair =>
                _cacheRefreshBookEpochs.GetValueOrDefault(pair.Key) == pair.Value);
        }
    }

    private CachedChapterListItemViewModel CreateChapterItem(
        CachedChapterCatalogItem chapter,
        IReadOnlyDictionary<int, bool>? selectionSnapshot = null,
        IReadOnlyDictionary<int, CachedChapterDecoration>? decorationSnapshot = null)
    {
        var decoration = decorationSnapshot is not null
            ? decorationSnapshot.GetValueOrDefault(chapter.ChapterIndex) ??
              CachedChapterDecoration.Placeholder(chapter.Title)
            : _chapterRefreshOverrides.TryGet(chapter.ChapterIndex, out var refreshOverride)
                ? refreshOverride
                : CachedChapterDecoration.Placeholder(chapter.Title);
        var coverage = new ChapterCacheStatus(
            chapter.ChapterIndex,
            decoration.CachedSegmentCount,
            decoration.CurrentConfigurationSegmentCount)
        {
            Kind = decoration.CurrentConfigurationStatus
        };
        var exportAvailability = GetExportAvailability(coverage);
        return new CachedChapterListItemViewModel(
            chapter.BookId,
            chapter.ChapterIndex,
            $"第 {chapter.ChapterIndex + 1} 章",
            decoration.Title,
            CacheCleanupFeedbackFormatter.FormatBytes(decoration.TotalSizeBytes),
            $"{decoration.EntryCount} 条缓存",
            CacheManagementCompletenessFormatter.Format(coverage),
            exportAvailability.IsExportable,
            exportAvailability.StatusText,
            exportAvailability.ToolTip,
            (selectionSnapshot is not null
                ? selectionSnapshot.TryGetValue(chapter.ChapterIndex, out var selectedSnapshot) && selectedSnapshot
                : _chapterSelectionDecorations.TryGet(chapter.ChapterIndex, out var selected) && selected));
    }

    private static CachedBookListItemViewModel CreateBookItem(
        CachedBookSummary book,
        bool isSelected = false) =>
        new(
            book.BookId,
            book.Title,
            book.Author,
            CacheCleanupFeedbackFormatter.FormatBytes(book.TotalSizeBytes),
            $"已缓存 {book.ChapterCount} 章",
            isSelected,
            book.TotalSizeBytes);

    private int FindBookInsertionPosition(
        CachedBookListItemViewModel updatedBook,
        int currentPosition)
    {
        var itemCountWithoutCurrent = Books.Count - 1;
        var low = 0;
        var high = itemCountWithoutCurrent;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var actualIndex = middle >= currentPosition ? middle + 1 : middle;
            if (CompareBookOrder(updatedBook, Books[actualIndex]) < 0)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }

    private static int CompareBookOrder(
        CachedBookListItemViewModel left,
        CachedBookListItemViewModel right)
    {
        var sizeComparison = right.TotalSizeBytes.CompareTo(left.TotalSizeBytes);
        return sizeComparison != 0
            ? sizeComparison
            : StringComparer.Ordinal.Compare(left.BookId, right.BookId);
    }

    private void ReportCacheRefreshFailure(
        Exception exception,
        CancellationToken activationToken)
    {
        if (IsCurrentPageActivation(activationToken))
        {
            _feedbackService.ShowProjectedNotification(
                "刷新缓存管理列表失败",
                _feedbackService.Project(exception));
        }
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
        var replacements = new List<(int Index, CachedChapterListItemViewModel Item)>(e.ChangedItems.Count);
        foreach (var chapterIndex in e.ChangedItems)
        {
            if (_chapterCatalog.TryGetPosition(chapterIndex, out var position))
            {
                if (_chapterSelection.IsSelected(chapterIndex))
                {
                    _chapterSelectionDecorations.Set(chapterIndex, true);
                }
                else
                {
                    _chapterSelectionDecorations.Remove(chapterIndex);
                }

                if (_chapterCatalogProjectionPending || _chapters.IsProjectionPending)
                {
                    _pendingChapterRowRefreshes.Add(chapterIndex);
                    continue;
                }

                if (position < _chapters.Count)
                {
                    replacements.Add(
                        (position,
                         _chapters[position].WithSelection(_chapterSelection.IsSelected(chapterIndex))));
                }
            }
        }

        if (replacements.Count > SelectionDecorationResetThreshold)
        {
            _chapters.ReplaceAtMany(replacements);
        }
        else
        {
            foreach (var replacement in replacements)
            {
                _chapters.ReplaceAt(replacement.Index, replacement.Item);
            }
        }

        OnPropertyChanged(nameof(SelectedChapterIndices));
        OnPropertyChanged(nameof(ChapterSelectionSummary));
        NotifyCommandStateChanged();
    }

    private void ShowCleanupFeedback(AudioCacheStoreCleanupResult result)
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

        return selectedIndices.All(
            index => _chapterCatalog.TryGetPosition(index, out var position) &&
                     _chapters[position].IsExportable);
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
                    exception => ReportExportProjectionFailure(exception, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ReportExportProjectionFailure(exception, cancellationToken);
            }

            return;
        }

        NotifyCommandStateChanged();
    }

    private void ReportExportProjectionFailure(
        Exception exception,
        CancellationToken activationToken)
    {
        if (IsCurrentPageActivation(activationToken))
        {
            _feedbackService.ShowProjectedNotification(
                "更新导出状态失败",
                _feedbackService.Project(exception));
        }
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

    private bool IsCurrentPageActivation(CancellationToken activationToken)
    {
        lock (_cacheRefreshSync)
        {
            return _isPageActive &&
                   _pageCancellation is { IsCancellationRequested: false } pageCancellation &&
                   pageCancellation.Token == activationToken;
        }
    }

    private bool IsChapterExportActive() =>
        _chapterExportCoordinator.CurrentSnapshot?.Status is
            ChapterExportBatchStatus.Waiting or
            ChapterExportBatchStatus.Running or
            ChapterExportBatchStatus.Cancelling;

    private static ChapterExportAvailability GetExportAvailability(ChapterCacheStatus coverage)
    {
        if (coverage.TotalSegmentCount is null)
        {
            return new ChapterExportAvailability(
                false,
                "当前配置不可用，无法导出",
                "无法读取当前 TTS 与文本配置对应的章节缓存。");
        }

        var total = coverage.TotalSegmentCount.Value;
        if (total == 0)
        {
            return new ChapterExportAvailability(
                false,
                "没有可播放段落，无法导出",
                "当前文本配置下没有可播放段落。");
        }

        if (coverage.CachedSegmentCount != total)
        {
            return new ChapterExportAvailability(
                false,
                "缓存不完整，无法导出",
                $"当前配置缓存为 {coverage.CachedSegmentCount}/{total} 段，请先完成缓存。");
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
