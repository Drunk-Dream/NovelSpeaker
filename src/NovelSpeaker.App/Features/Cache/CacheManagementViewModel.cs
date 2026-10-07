using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.Export;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.Cache;

public sealed partial class CacheManagementViewModel : ObservableObject, ITransientEscapeHandler
{
    private const string CleanupImpactMessage = "此操作只会清理音频缓存，不会删除书籍、章节、阅读进度、语音服务或章节规则。";
    private const int ChapterDecorationWindowSize = 32;
    private const int SelectionDecorationResetThreshold = 64;

    private readonly IAudioCacheStore _cacheStore;
    private readonly ICacheReadModel _readModel;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly IAppNavigator _navigator;
    private readonly IUiScheduler _uiScheduler;
    private readonly CacheManagementExportController _exportController;
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
    private readonly object _pageSync = new();
    private readonly SemaphoreSlim _projectionGate = new(1, 1);
    private CancellationTokenSource? _chapterLoadCts;
    private CancellationTokenSource? _chapterDecorationCts;
    private CancellationTokenSource? _bookProjectionCts;
    private CancellationTokenSource? _pageCancellation;
    private int _bookLoadVersion;
    private int _chapterLoadVersion;
    private int _chapterDecorationRequestVersion;
    private bool _isPageActive;
    private bool _isReadModelSubscribed;
    private bool _chapterCatalogProjectionPending;
    private string? _selectedBookId;
    private string? _selectedBookDecoration;

    public CacheManagementViewModel(
        IAudioCacheStore cacheStore,
        ICacheReadModel readModel,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        IAppNavigator navigator,
        IChapterExportCoordinator chapterExportCoordinator,
        IPresentationFileDialogService fileDialogs,
        IUiScheduler? uiScheduler = null)
    {
        _cacheStore = cacheStore;
        _readModel = readModel;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _navigator = navigator;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        _exportController = new CacheManagementExportController(
            chapterExportCoordinator,
            dialogService,
            feedbackService,
            fileDialogs,
            _uiScheduler);
        _exportController.StateChanged += OnExportStateChanged;
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
        var decorationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previousDecorationCts = _chapterDecorationCts;
        _chapterDecorationCts = decorationCts;
        previousDecorationCts?.Cancel();
        _pageTasks.Register(
            RefreshChapterDecorationsOwnedAsync(
                bookId,
                chapterIndices,
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
        !_exportController.IsBatchActive &&
        HasSelection &&
        !string.IsNullOrWhiteSpace(_selectedBookId) &&
        _chapterSelection.Count > 0;

    public string ExportCommandToolTip
    {
        get
        {
            if (_exportController.IsBatchActive)
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
        var activationToken = _pageCancellation!.Token;
        await _projectionGate.WaitAsync(activationToken);
        try
        {
            await LoadBooksAsync(activationToken);
            activationToken.ThrowIfCancellationRequested();
            ClearSelection();
        }
        finally
        {
            _projectionGate.Release();
        }
    }

    public void HandleNavigatedFrom()
    {
        Interlocked.Increment(ref _bookLoadVersion);
        Interlocked.Increment(ref _chapterLoadVersion);
        _bookProjectionCts?.Cancel();
        _bookProjectionCts = null;
        CancelChapterLoad();
        IsLoadingChapters = false;
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
        if (item is null || IsBusy || IsLoadingBooks)
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

        IsBusy = true;
        NotifyCommandStateChanged();

        try
        {
            await _exportController.PrepareAndStartAsync(
                selectedBookId,
                SelectedBookTitle,
                selectedChapters
                    .Select(static chapter => new CacheManagementExportChapter(
                        chapter.ChapterIndex,
                        chapter.Title,
                        chapter.IsExportable))
                    .ToArray(),
                cancellationToken);
        }
        finally
        {
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

    private async Task LoadBooksAsync(CancellationToken cancellationToken)
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
            var books = (await _readModel.GetBooksAsync(projectionCts.Token)).Value;
            projectionCts.Token.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _bookLoadVersion) || !IsCurrentPageActivation(cancellationToken))
            {
                return;
            }

            var selectedBookId = _selectedBookId;
            Func<bool> projectionIsCurrent = () =>
                version == Volatile.Read(ref _bookLoadVersion) && IsCurrentPageActivation(cancellationToken);
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
        CancelChapterLoad();
        _chapterLoadCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _pageCancellation?.Token ?? CancellationToken.None);
        var localCts = _chapterLoadCts;
        var version = Interlocked.Increment(ref _chapterLoadVersion);
        InvalidateChapterDecorationRequests();
        Func<bool> projectionIsCurrent = () =>
            IsCurrentSelectedBook(bookId, version, localCts.Token);

        var projectionEntered = false;
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
            await _projectionGate.WaitAsync(localCts.Token);
            projectionEntered = true;
            InvalidateChapterDecorationRequests();
            var chapters = (await _readModel.GetBookAsync(bookId, localCts.Token)).Value.Chapters;
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
            try
            {
                await projectionTask;
            }
            finally
            {
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
            if (projectionEntered) _projectionGate.Release();
            if (version == Volatile.Read(ref _chapterLoadVersion))
            {
                IsLoadingChapters = false;
                NotifyVisibilityStateChanged();
            }
            if (ReferenceEquals(_chapterLoadCts, localCts)) _chapterLoadCts = null;
            localCts.Dispose();
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
            var result = await _readModel.GetChaptersAsync(bookId, chapterIndices, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentSelectedBook(bookId, chapterLoadVersion, cancellationToken) ||
                requestVersion != Volatile.Read(ref _chapterDecorationRequestVersion))
            {
                return;
            }

            var viewsByIndex = result.Value.ToDictionary(static chapter => chapter.ChapterIndex);
            foreach (var chapterIndex in chapterIndices)
            {
                if (!_chapterCatalog.TryGetPosition(chapterIndex, out var position))
                {
                    continue;
                }

                if (viewsByIndex.TryGetValue(chapterIndex, out var view) &&
                    (!_chapterRefreshOverrides.TryGet(chapterIndex, out var current) || current.Revision <= result.Revision))
                {
                    _chapterRefreshOverrides.Set(chapterIndex, CachedChapterDecoration.From(view, _chapterCatalog[position].Title, result.Revision));
                }
                RefreshChapterRow(chapterIndex);
            }
            NotifyCommandStateChanged();
        }
        finally
        {
            _chapterDecorationQueryGate.Release();
        }
    }

    private async Task RefreshChapterDecorationsOwnedAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        int requestVersion,
        CancellationTokenSource decorationCts)
    {
        try
        {
            await RefreshChapterDecorationsAsync(
                bookId,
                chapterIndices,
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

    private void CancelChapterLoad()
    {
        _chapterLoadCts?.Cancel();
        _chapterLoadCts = null;
    }

    private void ActivatePage(CancellationToken cancellationToken)
    {
        DeactivatePage();
        lock (_pageSync)
        {
            _pageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isPageActive = true;
        }
        _readModel.Changed += OnReadModelChanged;
        _isReadModelSubscribed = true;
        _exportController.Activate(_pageCancellation.Token);
    }

    private void DeactivatePage()
    {
        if (_isReadModelSubscribed)
        {
            _readModel.Changed -= OnReadModelChanged;
            _isReadModelSubscribed = false;
        }
        _exportController.Deactivate();
        CancellationTokenSource? pageCancellation;
        lock (_pageSync)
        {
            pageCancellation = _pageCancellation;
            _pageCancellation = null;
            _isPageActive = false;
        }
        InvalidateChapterDecorationRequests();
        _pendingChapterRowRefreshes.Clear();
        _chapterCatalogProjectionPending = false;
        pageCancellation?.Cancel();
        pageCancellation?.Dispose();
    }

    private void InvalidateChapterDecorationRequests()
    {
        Interlocked.Increment(ref _chapterDecorationRequestVersion);
        _chapterDecorationWindow.Clear();
        _chapterDecorationCts?.Cancel();
    }

    private void OnReadModelChanged(object? sender, CacheReadModelChange change)
    {
        if (!TryGetActivePageCancellationToken(out var activationToken)) return;
        _pageTasks.Register(ProjectReadModelChangeAsync(change, activationToken),
            exception => ReportCacheRefreshFailure(exception, activationToken));
    }

    // Serializes UI projections, not Cache invalidation/repair. Each immutable change is queried
    // once; the Cache owner supplies a consistent result. No dirty/requeue algorithm lives here.
    private async Task ProjectReadModelChangeAsync(CacheReadModelChange change, CancellationToken activationToken)
    {
        await _projectionGate.WaitAsync(activationToken).ConfigureAwait(false);
        try
        {
            await _uiScheduler.InvokeAsync(async () =>
            {
                if (!IsCurrentPageActivation(activationToken)) return;
                var global = change.Scopes.Any(static scope => scope is CacheReadModelScope.Global);
                var bookIds = change.Scopes.Select(static scope => scope switch
                {
                    CacheReadModelScope.Book book => book.BookId,
                    CacheReadModelScope.Chapters chapters => chapters.BookId,
                    _ => null
                }).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
                await RefreshBookSummariesAsync(global ? null : bookIds, activationToken);
                if (!IsCurrentPageActivation(activationToken) || _selectedBookId is not { } bookId) return;

                var version = Volatile.Read(ref _chapterLoadVersion);
                if (global || change.Scopes.Any(scope => scope is CacheReadModelScope.Book book && book.BookId == bookId))
                {
                    var catalog = (await _readModel.GetBookAsync(bookId, activationToken)).Value;
                    if (!IsCurrentSelectedBook(bookId, version, activationToken)) return;
                    await ReconcileChapterCatalogAsync(catalog.Chapters, bookId, version, activationToken);
                    if (IsCurrentSelectedBook(bookId, version, activationToken)) RefreshCurrentWindow(activationToken);
                }
                else
                {
                    var indices = change.Scopes.OfType<CacheReadModelScope.Chapters>()
                        .Where(scope => scope.BookId == bookId)
                        .SelectMany(static scope => scope.ChapterIndices).Distinct().ToArray();
                    if (indices.Length == 0) return;
                    var result = await _readModel.GetChaptersAsync(bookId, indices, activationToken);
                    if (!IsCurrentSelectedBook(bookId, version, activationToken)) return;
                    await ReconcileAffectedChaptersAsync(result, bookId, version, activationToken);
                }
            }, activationToken).ConfigureAwait(false);
        }
        finally
        {
            _projectionGate.Release();
        }
    }

    private void RefreshCurrentWindow(CancellationToken activationToken)
    {
        var indices = _chapterDecorationWindow.Count > 0
            ? _chapterDecorationWindow.ToArray()
            : _chapterCatalog.Slice(0, Math.Min(ChapterDecorationWindowSize, _chapterCatalog.Count))
                .Select(static chapter => chapter.ChapterIndex).ToArray();
        RequestChapterDecorationIndicesOnUi(indices, activationToken, forceRefresh: true);
    }

    private async Task ReconcileAffectedChaptersAsync(
        CacheReadResult<IReadOnlyList<CacheChapterView>> result, string bookId, int version, CancellationToken activationToken)
    {
        var views = result.Value;
        var previous = _chapterCatalog;
        var catalogChanged = views.Any(view => view.Physical is { } physical
            ? !previous.TryGet(view.ChapterIndex, out var entry) || entry.Title != physical.Title
            : previous.TryGetPosition(view.ChapterIndex, out _));
        if (catalogChanged)
        {
            var changes = views.ToDictionary(static view => view.ChapterIndex);
            var entries = await Task.Run(() => previous.Items
                .Where(entry => !changes.ContainsKey(entry.ChapterIndex))
                .Concat(views.Where(static view => view.Physical is not null).Select(view =>
                    new CachedChapterCatalogItem(bookId, view.Physical!.Title, view.ChapterIndex)))
                .OrderBy(static entry => entry.ChapterIndex)
                .Select(entry => new CachedChapterCatalogEntry(entry.BookId, entry.ChapterIndex, entry.Title))
                .ToArray(), activationToken);
            if (!IsCurrentSelectedBook(bookId, version, activationToken)) return;
            await ReconcileChapterCatalogAsync(entries, bookId, version, activationToken);
        }
        if (!IsCurrentSelectedBook(bookId, version, activationToken)) return;
        foreach (var view in views)
        {
            if (_chapterDecorationWindow.Contains(view.ChapterIndex) &&
                _chapterCatalog.TryGet(view.ChapterIndex, out var entry) &&
                (!_chapterRefreshOverrides.TryGet(view.ChapterIndex, out var current) || current.Revision <= result.Revision))
            {
                _chapterRefreshOverrides.Set(view.ChapterIndex, CachedChapterDecoration.From(view, entry.Title, result.Revision));
                RefreshChapterRow(view.ChapterIndex);
            }
        }
        if (catalogChanged) RefreshCurrentWindow(activationToken);
        NotifyCommandStateChanged();
    }

    private async Task ReconcileChapterCatalogAsync(
        IReadOnlyList<CachedChapterCatalogEntry> chapters, string bookId, int version, CancellationToken activationToken)
    {
        var previous = _chapterCatalog;
        var delta = await Task.Run(() => CreateChapterCatalogDelta(previous, CreateChapterProjection(chapters)), activationToken);
        if (!IsCurrentSelectedBook(bookId, version, activationToken)) return;
        await ApplyChapterCatalogDeltaAsync(delta, () => IsCurrentSelectedBook(bookId, version, activationToken), activationToken);
        SelectedBookHasCache = _chapterCatalog.Count > 0;
        NotifyVisibilityStateChanged();
        NotifyCommandStateChanged();
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
        Func<bool> isCurrent,
        CancellationToken cancellationToken)
    {
        if (delta.ChangeCount == 0) return;
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
                    isCurrent: isCurrent,
                    beforeComplete: () =>
                    {
                        if (!isCurrent())
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
        if (delta.RemovedKeys.Count > 0 || delta.Added.Count > 0)
        {
            _chapterSelection.UpdateIndexedItems(
                delta.Catalog.Keys,
                delta.Catalog.Positions);
        }
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

    private bool IsCurrentSelectedBook(string bookId, int version, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && _isPageActive &&
        version == Volatile.Read(ref _chapterLoadVersion) && bookId == _selectedBookId;

    private async Task RefreshBookSummariesAsync(IReadOnlyCollection<string>? bookIds, CancellationToken activationToken)
    {
        var books = (bookIds is null
            ? await _readModel.GetBooksAsync(activationToken)
            : await _readModel.GetBooksAsync(bookIds, activationToken)).Value;
        if (!IsCurrentPageActivation(activationToken)) return;
        var positions = _bookPositions;
        var previousRows = _books.ToArray();
        var selectedAtProjection = _selectedBookId;
        var projection = await Task.Run(() =>
        {
            var byId = books.ToDictionary(static book => book.BookId, StringComparer.Ordinal);
            var affected = bookIds ?? positions.Keys.Concat(byId.Keys).Distinct(StringComparer.Ordinal).ToArray();
            var changes = new List<(string BookId, CachedBookListItemViewModel? Item)>();
            foreach (var bookId in affected)
            {
                if (!byId.TryGetValue(bookId, out var book))
                {
                    if (positions.ContainsKey(bookId)) changes.Add((bookId, null));
                    continue;
                }
                if (!positions.TryGetValue(bookId, out var position) ||
                    !HasSameBookDisplay(previousRows[position], book))
                {
                    changes.Add((bookId, CreateBookItem(book, bookId == selectedAtProjection)));
                }
            }

            CachedBookListItemViewModel[]? replacement = null;
            Dictionary<string, int>? replacementPositions = null;
            if (changes.Count > 512)
            {
                var next = previousRows.ToDictionary(static item => item.BookId, StringComparer.Ordinal);
                foreach (var (bookId, item) in changes)
                {
                    if (item is null) next.Remove(bookId);
                    else next[bookId] = item;
                }
                replacement = next.Values.OrderByDescending(static item => item.TotalSizeBytes)
                    .ThenBy(static item => item.BookId, StringComparer.Ordinal).ToArray();
                replacementPositions = replacement.Select((item, position) => (item.BookId, position))
                    .ToDictionary(static item => item.BookId, static item => item.position, StringComparer.Ordinal);
            }
            return (ById: byId, Changes: changes, Replacement: replacement, Positions: replacementPositions);
        }, activationToken);
        if (!IsCurrentPageActivation(activationToken)) return;

        if (projection.Replacement is { } replacement)
        {
            IsLoadingBooks = true;
            try
            {
                await _books.ReplaceWithInBatchesAsync(
                    replacement,
                    item => item.WithSelection(item.BookId == selectedAtProjection),
                    _uiScheduler,
                    activationToken,
                    preservePreviousItemsOnCancel: true,
                    isCurrent: () => IsCurrentPageActivation(activationToken),
                    beforeComplete: () =>
                    {
                        _bookPositions = projection.Positions!;
                        _selectedBookDecoration = selectedAtProjection;
                        if (_selectedBookId is { } selected && !_bookPositions.ContainsKey(selected)) ClearSelection();
                        else
                        {
                            UpdateBookSelection(_selectedBookId);
                        }
                    });
            }
            finally
            {
                if (IsCurrentPageActivation(activationToken)) IsLoadingBooks = false;
            }
        }
        else
        {
            foreach (var (bookId, item) in projection.Changes)
            {
                if (item is null)
                {
                    var position = _bookPositions[bookId];
                    var updatedPositions = await GetUpdatedBookPositionsAsync(bookId, position, -1, activationToken);
                    if (!IsCurrentPageActivation(activationToken)) return;
                    _books.RemoveAt(position);
                    _bookPositions = updatedPositions;
                    if (_selectedBookId == bookId) ClearSelection();
                    continue;
                }
                if (_bookPositions.TryGetValue(bookId, out var positionToUpdate))
                {
                    var targetPosition = FindBookInsertionPosition(item, positionToUpdate);
                    if (targetPosition != positionToUpdate)
                    {
                        var updatedPositions = await GetUpdatedBookPositionsAsync(bookId, positionToUpdate, targetPosition, activationToken);
                        if (!IsCurrentPageActivation(activationToken)) return;
                        _books.Move(positionToUpdate, targetPosition);
                        _bookPositions = updatedPositions;
                    }
                    _books.ReplaceAt(targetPosition, item.WithSelection(_selectedBookId == bookId));
                }
                else
                {
                    var targetPosition = FindBookInsertionPosition(item, _books.Count);
                    var updatedPositions = await GetUpdatedBookPositionsAsync(bookId, -1, targetPosition, activationToken);
                    if (!IsCurrentPageActivation(activationToken)) return;
                    _books.Insert(targetPosition, item.WithSelection(_selectedBookId == bookId));
                    _bookPositions = updatedPositions;
                }
            }
        }

        if (!IsCurrentPageActivation(activationToken)) return;
        if (_selectedBookId is { } selectedBookId && projection.ById.TryGetValue(selectedBookId, out var selectedBook))
        {
            SelectedBookTitle = selectedBook.Title;
            SelectedBookAuthor = selectedBook.Author ?? "未知作者";
            SelectedBookCacheSizeText = CacheCleanupFeedbackFormatter.FormatBytes(selectedBook.TotalSizeBytes);
            SelectedBookChapterCountText = $"已缓存 {selectedBook.ChapterCount} 章";
        }
        NotifyVisibilityStateChanged();
        NotifyCommandStateChanged();
    }

    private static bool HasSameBookDisplay(CachedBookListItemViewModel previous, CachedBookSummary summary) =>
        previous.Title == summary.Title &&
        previous.Author == (string.IsNullOrWhiteSpace(summary.Author) ? "未知作者" : summary.Author.Trim()) &&
        previous.TotalSizeBytes == summary.TotalSizeBytes && previous.ChapterCountText == $"已缓存 {summary.ChapterCount} 章";

    private async Task<Dictionary<string, int>> GetUpdatedBookPositionsAsync(string bookId, int from, int to, CancellationToken activationToken)
    {
        var previous = _bookPositions;
        return await Task.Run(() =>
        {
            var result = new Dictionary<string, int>(previous.Count + 1, StringComparer.Ordinal);
            foreach (var (id, position) in previous)
            {
                if (id == bookId) continue;
                var next = position;
                if (from >= 0 && next > from) next--;
                if (to >= 0 && next >= to) next++;
                result[id] = next;
            }
            if (to >= 0) result[bookId] = to;
            return result;
        }, activationToken);
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
        var exportAvailability = GetExportAvailability(coverage, decoration.IsExportable);
        return new CachedChapterListItemViewModel(
            chapter.BookId,
            chapter.ChapterIndex,
            decoration.Title,
            CacheCleanupFeedbackFormatter.FormatBytes(decoration.TotalSizeBytes),
            $"{decoration.EntryCount} 条缓存",
            CacheManagementCompletenessFormatter.Format(coverage),
            decoration.IsExportable,
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
        var itemCountWithoutCurrent = Books.Count - (currentPosition < Books.Count ? 1 : 0);
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

    private void OnExportStateChanged(object? sender, EventArgs e) => NotifyCommandStateChanged();

    private bool TryGetActivePageCancellationToken(out CancellationToken cancellationToken)
    {
        lock (_pageSync)
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
        lock (_pageSync)
        {
            return _isPageActive &&
                   _pageCancellation is { IsCancellationRequested: false } pageCancellation &&
                   pageCancellation.Token == activationToken;
        }
    }

    private static ChapterExportAvailability GetExportAvailability(ChapterCacheStatus coverage, bool isExportable)
    {
        if (coverage.TotalSegmentCount is null)
        {
            return new ChapterExportAvailability(
                "当前配置不可用，无法导出",
                "无法读取当前 TTS 与文本配置对应的章节缓存。");
        }

        var total = coverage.TotalSegmentCount.Value;
        if (total == 0)
        {
            return new ChapterExportAvailability(
                "没有可播放段落，无法导出",
                "当前文本配置下没有可播放段落。");
        }

        if (!isExportable)
        {
            return new ChapterExportAvailability(
                "缓存不完整，无法导出",
                $"当前配置缓存为 {coverage.CachedSegmentCount}/{total} 段，请先完成缓存。");
        }

        return new ChapterExportAvailability(
            "可导出",
            $"当前配置缓存完整（{total}/{total} 段），可导出为 MP3。");
    }

    private sealed record ChapterExportAvailability(
        string StatusText,
        string ToolTip);
}
