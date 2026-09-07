using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Features.Books.Shared;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.Books.Library;

/// <summary>
/// Owns page state and commands while the library catalog and card projection remain
/// separate lifetimes.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private const int BackgroundRowLayoutThreshold = 512;

    private static readonly IReadOnlyList<LibrarySortOption> SortOptions =
    [
        new(LibrarySortMode.RecentReading, "最近阅读"),
        new(LibrarySortMode.Title, "书名")
    ];

    private readonly IBookLibraryQuery _bookLibraryQuery;
    private readonly IBookDeletionService _bookDeletionService;
    private readonly ILibraryImportCoordinator _libraryImportCoordinator;
    private readonly IBookDeleteDialogService _deleteDialogService;
    private readonly IBookCatalogInvalidationState _catalogInvalidationState;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppNavigator _navigator;
    private readonly IPlaybackBookCommands _playbackCoordinator;
    private readonly IUiScheduler _uiScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly IBookCoverGenerator _bookCoverGenerator;
    private readonly OwnedTaskRegistry _pageTasks = new();
    private readonly ResettableObservableCollection<LibraryBookCardProjection> _books = [];
    private readonly ResettableObservableCollection<LibraryBookRowProjection> _rows = [];
    private readonly Dictionary<string, EffectiveReadingProgress> _playbackDecorations =
        new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, int> _visibleBookPositions =
        new Dictionary<string, int>(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, LibraryBookRowPosition> _visibleBookRowPositions =
        new Dictionary<string, LibraryBookRowPosition>(StringComparer.Ordinal);
    private IReadOnlyList<LibraryBookCardProjection> _visibleBookProjection = [];
    private CancellationTokenSource? _searchDebounceCancellationTokenSource;
    private CancellationTokenSource? _activeProjectionCancellationTokenSource;
    private CancellationTokenSource? _activeRowLayoutCancellationTokenSource;
    private CancellationTokenSource? _activeImportCancellationTokenSource;
    private LibraryBookCatalog _catalog;
    private PlaybackSnapshot _lastPlaybackSnapshot;
    private int _searchVersion;
    private int _projectionVersion;
    private int _loadVersion;
    private int _importVersion;
    private int _playbackProjectionVersion;
    private int _playbackSnapshotVersion;
    private int _rowLayoutVersion;
    private bool _isDeletingBook;
    private bool _isPageEventsRegistered;
    private bool _refreshVisibleProjectionOnNextActivation;
    private bool _refreshRowsOnNextActivation;
    private double _availableWidth;

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
        LibraryScrollState scrollState,
        IUiScheduler? uiScheduler = null,
        TimeProvider? timeProvider = null)
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
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        _timeProvider = timeProvider ?? TimeProvider.System;
        ScrollState = scrollState;
        _catalog = new LibraryBookCatalog([]);
        _lastPlaybackSnapshot = playbackCoordinator.CurrentSnapshot;
    }

    public ObservableCollection<LibraryBookCardProjection> Books => _books;

    public ObservableCollection<LibraryBookRowProjection> Rows => _rows;

    public IReadOnlyDictionary<string, int> VisibleBookPositions => _visibleBookPositions;

    public IReadOnlyDictionary<string, LibraryBookRowPosition> VisibleBookRowPositions => _visibleBookRowPositions;

    public IReadOnlyList<LibrarySortOption> AvailableSortOptions => SortOptions;

    public LibraryScrollState ScrollState { get; }

    public int ColumnCount { get; private set; }

    public double CardWidth { get; private set; }

    internal void SetAvailableWidth(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0d ||
            Math.Abs(availableWidth - _availableWidth) < 0.1d)
        {
            return;
        }

        _availableWidth = availableWidth;
        ScheduleRowsRebuild();
    }

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

    public async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var loadVersion = Interlocked.Increment(ref _loadVersion);
        InvalidateVisibleProjection();
        var summaries = await _bookLibraryQuery.GetBooksAsync(cancellationToken);
        if (!IsCurrentLoad(loadVersion, cancellationToken))
        {
            return false;
        }

        var catalog = summaries.Count >= 512
            ? await Task.Run(
                () => new LibraryBookCatalog(summaries.ToArray()),
                cancellationToken).ConfigureAwait(true)
            : new LibraryBookCatalog(summaries.ToArray());
        if (!IsCurrentLoad(loadVersion, cancellationToken))
        {
            return false;
        }

        var playbackSnapshot = _playbackCoordinator.CurrentSnapshot;
        var decorations = new Dictionary<string, EffectiveReadingProgress>(StringComparer.Ordinal);
        while (!await ProjectVisibleBooksAsync(
            cancellationToken,
            catalog,
            playbackSnapshot,
            decorations))
        {
            if (!IsCurrentLoad(loadVersion, cancellationToken))
            {
                return false;
            }
        }
        if (!IsCurrentLoad(loadVersion, cancellationToken))
        {
            return false;
        }

        ApplyPlaybackSnapshot(_playbackCoordinator.CurrentSnapshot);
        _catalogInvalidationState.Consume();
        return true;
    }

    public async Task ImportFilesAsync(IReadOnlyList<string>? filePaths, CancellationToken cancellationToken)
    {
        var selectedPath = GetSingleImportPath(filePaths);
        if (selectedPath is null)
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
            var outcome = await _libraryImportCoordinator.ImportAsync(selectedPath, progress, activeCancellationTokenSource.Token);
            if (!IsCurrentImport(version, activeCancellationTokenSource))
            {
                return;
            }

            if (outcome.Status == LibraryImportCoordinatorStatus.Imported)
            {
                if (!await LoadAsync(activeCancellationTokenSource.Token) ||
                    !IsCurrentImport(version, activeCancellationTokenSource))
                {
                    return;
                }

                _feedbackService.ShowSuccess("导入成功", "已导入小说。");
            }
            else if (outcome.Status == LibraryImportCoordinatorStatus.Failed)
            {
                ShowImportFailure(outcome.FailureReason);
            }
            else if (outcome.Status == LibraryImportCoordinatorStatus.InvalidSource)
            {
                _feedbackService.ShowWarning("无法导入", "只支持导入单个 .txt 文件。");
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
        _activeImportCancellationTokenSource?.Cancel();
        _activeImportCancellationTokenSource?.Dispose();
        _activeImportCancellationTokenSource = null;
        ImportStatusMessage = string.Empty;
        IsBusy = false;
    }

    public void HandleNavigatedTo()
    {
        RegisterPageEvents();
        RebuildVisibleBookIndex();
        var refreshVisibleProjection = _refreshVisibleProjectionOnNextActivation;
        _refreshVisibleProjectionOnNextActivation = false;
        if (refreshVisibleProjection)
        {
            _refreshRowsOnNextActivation = false;
            ScheduleVisibleProjection();
        }
        else if (_refreshRowsOnNextActivation)
        {
            _refreshRowsOnNextActivation = false;
            ScheduleRowsRebuild();
        }

        ApplyPlaybackSnapshot(_playbackCoordinator.CurrentSnapshot);
    }

    public void HandleNavigatedFrom()
    {
        var projectionWasActive = _activeProjectionCancellationTokenSource is not null;
        var projectionWasPending = _searchDebounceCancellationTokenSource is not null;
        var rowLayoutWasActive = _activeRowLayoutCancellationTokenSource is not null;
        Interlocked.Increment(ref _loadVersion);
        CancelActiveImport();
        _searchDebounceCancellationTokenSource?.Cancel();
        _searchDebounceCancellationTokenSource?.Dispose();
        _searchDebounceCancellationTokenSource = null;
        InvalidateVisibleProjection();
        Interlocked.Increment(ref _searchVersion);
        if (rowLayoutWasActive)
        {
            _refreshRowsOnNextActivation = true;
        }

        if (projectionWasActive || projectionWasPending)
        {
            RestorePreviousVisibleProjection(
                Volatile.Read(ref _projectionVersion),
                rebuildRows: false);
            _refreshVisibleProjectionOnNextActivation = true;
        }

        if (!_isPageEventsRegistered)
        {
            return;
        }

        _playbackCoordinator.SnapshotChanged -= OnPlaybackSnapshotChanged;
        Interlocked.Increment(ref _playbackProjectionVersion);
        _isPageEventsRegistered = false;
    }

    private void RegisterPageEvents()
    {
        if (_isPageEventsRegistered)
        {
            return;
        }

        _playbackCoordinator.SnapshotChanged += OnPlaybackSnapshotChanged;
        Interlocked.Increment(ref _playbackProjectionVersion);
        _isPageEventsRegistered = true;
    }

    [RelayCommand]
    private Task OpenBook(LibraryBookCardProjection? book, CancellationToken cancellationToken)
    {
        if (book is null)
        {
            return Task.CompletedTask;
        }

        return _navigator.NavigateAsync(
            new PlayerRoute(book.BookId, AppRoutes.Library, PlayerNavigationMode.OpenPaused),
            cancellationToken);
    }

    [RelayCommand]
    private Task OpenBookDetails(LibraryBookCardProjection? book, CancellationToken cancellationToken)
    {
        if (book is null)
        {
            return Task.CompletedTask;
        }

        return _navigator.NavigateAsync(new BookDetailsRoute(book.BookId), cancellationToken);
    }

    [RelayCommand]
    private async Task DeleteBookAsync(LibraryBookCardProjection? book, CancellationToken cancellationToken)
    {
        if (book is null || _isDeletingBook)
        {
            return;
        }

        var decision = await _deleteDialogService.ShowAsync(
            new BookDeleteDialogRequest(
                book.Title,
                IsCurrentPlaybackBook(book.BookId)),
            cancellationToken);
        if (!decision.IsConfirmed)
        {
            return;
        }

        _isDeletingBook = true;
        try
        {
            if (IsCurrentPlaybackBook(book.BookId))
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
                if (!await LoadAsync(cancellationToken))
                {
                    return;
                }
                return;
            }

            _catalogInvalidationState.Invalidate();
            if (!await LoadAsync(cancellationToken))
            {
                return;
            }
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
        ScheduleVisibleProjection();
    }

    private void ScheduleFilterRefresh()
    {
        _searchDebounceCancellationTokenSource?.Cancel();
        _searchDebounceCancellationTokenSource?.Dispose();
        var debounceCancellation = new CancellationTokenSource();
        _searchDebounceCancellationTokenSource = debounceCancellation;
        var version = Interlocked.Increment(ref _searchVersion);
        InvalidateVisibleProjection();
        _pageTasks.Register(
            ApplyVisibleBooksAfterDebounceAsync(version, debounceCancellation),
            exception => _feedbackService.ShowProjectedNotification(
                "更新书库筛选失败",
                _feedbackService.Project(exception)));
    }

    private async Task ApplyVisibleBooksAfterDebounceAsync(
        int version,
        CancellationTokenSource debounceCancellation)
    {
        var cancellationToken = debounceCancellation.Token;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(120), _timeProvider, cancellationToken);
            if (version != Volatile.Read(ref _searchVersion))
            {
                return;
            }

            await ProjectVisibleBooksAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_searchDebounceCancellationTokenSource, debounceCancellation))
            {
                _searchDebounceCancellationTokenSource = null;
            }

            debounceCancellation.Dispose();
        }
    }

    private void ScheduleVisibleProjection()
    {
        _searchDebounceCancellationTokenSource?.Cancel();
        _searchDebounceCancellationTokenSource?.Dispose();
        _searchDebounceCancellationTokenSource = null;
        Interlocked.Increment(ref _searchVersion);
        InvalidateVisibleProjection();
        _pageTasks.Register(
            ProjectVisibleBooksAsync(CancellationToken.None),
            exception => _feedbackService.ShowProjectedNotification(
                "更新书库排序失败",
                _feedbackService.Project(exception)));
    }

    private async Task<bool> ProjectVisibleBooksAsync(
        CancellationToken cancellationToken,
        LibraryBookCatalog? sourceCatalog = null,
        PlaybackSnapshot? sourcePlaybackSnapshot = null,
        IReadOnlyDictionary<string, EffectiveReadingProgress>? sourceDecorations = null)
    {
        var version = Interlocked.Increment(ref _projectionVersion);
        CancelActiveProjection();
        using var projectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeProjectionCancellationTokenSource = projectionCancellation;
        try
        {
            var catalog = sourceCatalog ?? _catalog;
            var normalizedSearchTerm = LibraryBookCatalog.NormalizeSearchText(SearchText);
            var sortMode = SelectedSortMode;
            var playbackSnapshot = sourcePlaybackSnapshot ?? _playbackCoordinator.CurrentSnapshot;
            ApplyPlaybackSnapshot(playbackSnapshot);
            var playbackVersion = Volatile.Read(ref _playbackProjectionVersion);
            var playbackSnapshotVersion = Volatile.Read(ref _playbackSnapshotVersion);
            var decorations = sourceDecorations is null
                ? new Dictionary<string, EffectiveReadingProgress>(_playbackDecorations, StringComparer.Ordinal)
                : new Dictionary<string, EffectiveReadingProgress>(sourceDecorations, StringComparer.Ordinal);
            if (playbackSnapshot.BookId is not null && catalog.TryGet(playbackSnapshot.BookId, out var activeBook))
            {
                decorations[playbackSnapshot.BookId] = EffectiveReadingProgressProjector.Project(
                    activeBook.Summary,
                    playbackSnapshot);
            }

            var projectionItems = catalog.Count >= 512
                ? await Task.Run(
                    () => catalog.Query(
                        normalizedSearchTerm,
                        sortMode,
                        decorations,
                        projectionCancellation.Token),
                    projectionCancellation.Token).ConfigureAwait(true)
                : catalog.Query(
                    normalizedSearchTerm,
                    sortMode,
                    decorations,
                    projectionCancellation.Token);
            projectionCancellation.Token.ThrowIfCancellationRequested();
            var projectedVisibleBookPositions = new Dictionary<string, int>(
                projectionItems.Count,
                StringComparer.Ordinal);
            var projectedVisibleBookList = new List<LibraryBookCardProjection>(projectionItems.Count);
            var reusableVisibleBookPositions = _visibleBookPositions;
            var reusableVisibleBooks = _visibleBookProjection.ToArray();
            await _books.ReplaceWithInBatchesAsync(
                projectionItems,
                item =>
                {
                    var progress = GetEffectiveProgress(item, decorations, playbackSnapshot);
                    var remainingChapterText = BuildRemainingChapterText(
                        item.Summary.TotalChapterCount,
                        progress.RemainingChapterCount);
                    var lastPlayedAt = item.Summary.LastPlayedAt?.ToString("O");
                    LibraryBookCardProjection book;
                    if (reusableVisibleBookPositions.TryGetValue(item.BookId, out var existingPosition) &&
                        (uint)existingPosition < (uint)reusableVisibleBooks.Length &&
                        reusableVisibleBooks[existingPosition] is { } existingBook &&
                        existingBook.CanReuseFor(item.Summary.Title, item.DisplayAuthor, lastPlayedAt))
                    {
                        book = existingBook.HasSameEffectiveProgress(progress, remainingChapterText)
                            ? existingBook
                            : existingBook.WithEffectiveProgress(progress, remainingChapterText);
                    }
                    else
                    {
                        book = CreateBookItem(item, progress, remainingChapterText, lastPlayedAt);
                    }

                    projectedVisibleBookPositions.Add(book.BookId, projectedVisibleBookList.Count);
                    projectedVisibleBookList.Add(book);
                    return book;
                },
                _uiScheduler,
                projectionCancellation.Token,
                notifyEachBatch: false,
                preservePreviousItemsOnCancel: true).ConfigureAwait(true);
            projectionCancellation.Token.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _projectionVersion))
            {
                return false;
            }

            if (sourceCatalog is not null)
            {
                projectionCancellation.Token.ThrowIfCancellationRequested();
                if (version != Volatile.Read(ref _projectionVersion))
                {
                    return false;
                }

                _catalog = sourceCatalog;
                _playbackDecorations.Clear();
                _lastPlaybackSnapshot = playbackSnapshot;
                SetActivePlaybackDecoration(playbackSnapshot);
            }

            _visibleBookPositions = projectedVisibleBookPositions;
            _visibleBookProjection = projectedVisibleBookList.ToArray();
            OnPropertyChanged(nameof(VisibleBookPositions));
            await RebuildRowsForCurrentProjectionAsync(projectionCancellation.Token).ConfigureAwait(true);

            HasBooks = catalog.Count > 0;
            HasVisibleBooks = _books.Count > 0;
            LibrarySummaryText = BuildLibrarySummary(catalog.Count, sortMode);
            var latestSnapshot = _playbackCoordinator.CurrentSnapshot;
            if (playbackVersion != Volatile.Read(ref _playbackProjectionVersion) ||
                playbackSnapshotVersion != Volatile.Read(ref _playbackSnapshotVersion) ||
                !Equals(playbackSnapshot, latestSnapshot))
            {
                ReconcileVisiblePlaybackSnapshot(latestSnapshot, playbackSnapshot.BookId);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            RestorePreviousVisibleProjection(version);
            if (!cancellationToken.IsCancellationRequested &&
                version != Volatile.Read(ref _projectionVersion))
            {
                return false;
            }

            throw;
        }
        finally
        {
            if (ReferenceEquals(_activeProjectionCancellationTokenSource, projectionCancellation))
            {
                _activeProjectionCancellationTokenSource = null;
            }
        }
    }

    private EffectiveReadingProgress GetEffectiveProgress(
        LibraryBookCatalogItem item,
        IReadOnlyDictionary<string, EffectiveReadingProgress> decorations,
        PlaybackSnapshot playbackSnapshot)
    {
        return decorations.TryGetValue(item.BookId, out var decoration)
            ? decoration
            : EffectiveReadingProgressProjector.Project(item.Summary, playbackSnapshot);
    }

    private LibraryBookCardProjection CreateBookItem(
        LibraryBookCatalogItem item,
        EffectiveReadingProgress progress,
        string remainingChapterText,
        string? lastPlayedAt)
    {
        return new LibraryBookCardProjection(
            item.BookId,
            item.Summary.Title,
            item.DisplayAuthor,
            progress.CurrentChapterTitle,
            remainingChapterText,
            progress.OverallProgress,
            progress.HasReadingProgress,
            lastPlayedAt,
            _bookCoverGenerator,
            canDelete: true);
    }

    private string? GetSingleImportPath(IReadOnlyList<string>? filePaths)
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

        if (string.IsNullOrWhiteSpace(filePaths[0]))
        {
            _feedbackService.ShowWarning("无法导入", "只支持导入单个 .txt 文件。");
            return null;
        }

        return filePaths[0];
    }

    private void OnPlaybackSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        if (!_isPageEventsRegistered)
        {
            return;
        }

        var projectionVersion = Volatile.Read(ref _playbackProjectionVersion);
        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(() => ApplyPlaybackSnapshot(snapshot, projectionVersion)),
                exception => _feedbackService.ShowProjectedNotification(
                    "更新书库播放状态失败",
                    _feedbackService.Project(exception)));
            return;
        }

        ApplyPlaybackSnapshot(snapshot, projectionVersion);
    }

    private void ApplyPlaybackSnapshot(PlaybackSnapshot snapshot, int? expectedProjectionVersion = null)
    {
        if (expectedProjectionVersion is int projectionVersion &&
            (!_isPageEventsRegistered ||
             projectionVersion != Volatile.Read(ref _playbackProjectionVersion) ||
             !Equals(_playbackCoordinator.CurrentSnapshot, snapshot)))
        {
            return;
        }

        if (Equals(_lastPlaybackSnapshot, snapshot))
        {
            return;
        }

        var previousBookId = _lastPlaybackSnapshot.BookId;
        var currentBookId = snapshot.BookId;
        _lastPlaybackSnapshot = snapshot;
        Interlocked.Increment(ref _playbackSnapshotVersion);

        if (previousBookId is not null &&
            !string.Equals(previousBookId, currentBookId, StringComparison.Ordinal))
        {
            _playbackDecorations.Remove(previousBookId);
            UpdateVisibleBook(previousBookId);
        }

        if (currentBookId is not null && _catalog.TryGet(currentBookId, out var currentBook))
        {
            SetActivePlaybackDecoration(snapshot, currentBook);
            UpdateVisibleBook(currentBookId);
        }
    }

    private void ReconcileVisiblePlaybackSnapshot(
        PlaybackSnapshot snapshot,
        string? projectedBookId)
    {
        ApplyPlaybackSnapshot(snapshot);

        if (projectedBookId is not null &&
            !string.Equals(projectedBookId, snapshot.BookId, StringComparison.Ordinal))
        {
            _playbackDecorations.Remove(projectedBookId);
            UpdateVisibleBook(projectedBookId);
        }

        if (snapshot.BookId is not null)
        {
            SetActivePlaybackDecoration(snapshot);
            UpdateVisibleBook(snapshot.BookId);
        }
    }

    private void SetActivePlaybackDecoration(
        PlaybackSnapshot snapshot,
        LibraryBookCatalogItem? currentBook = null)
    {
        if (snapshot.BookId is null)
        {
            return;
        }

        currentBook ??= _catalog.TryGet(snapshot.BookId, out var resolvedBook)
            ? resolvedBook
            : null;
        if (currentBook is not null)
        {
            _playbackDecorations[snapshot.BookId] = EffectiveReadingProgressProjector.Project(
                currentBook.Summary,
                snapshot);
        }
    }

    private void RebuildVisibleBookIndex()
    {
        RebuildVisibleBookPositions();
    }

    private void RestorePreviousVisibleProjection(int projectionVersion, bool rebuildRows = true)
    {
        if (projectionVersion != Volatile.Read(ref _projectionVersion))
        {
            return;
        }

        var previousProjection = _visibleBookProjection;
        var isAlreadyRestored = _books.Count == previousProjection.Count;
        if (isAlreadyRestored)
        {
            for (var index = 0; index < previousProjection.Count; index++)
            {
                if (!ReferenceEquals(_books[index], previousProjection[index]))
                {
                    isAlreadyRestored = false;
                    break;
                }
            }
        }

        if (!isAlreadyRestored)
        {
            _books.ReplaceWith(previousProjection);
        }

        RebuildVisibleBookPositions();
        if (rebuildRows)
        {
            ScheduleRowsRebuild();
        }

        HasVisibleBooks = previousProjection.Count > 0;
    }

    private void UpdateVisibleBook(string bookId)
    {
        if (_books.IsProjectionPending || _books.IsReplacing)
        {
            // The visible id-to-position map intentionally remains on the last
            // committed projection while a batch replacement is in flight. Keep
            // the sparse decoration current and let the commit/abort reconciliation
            // update the card against the matching committed list.
            return;
        }

        if (!_visibleBookPositions.TryGetValue(bookId, out var position) ||
            (uint)position >= (uint)_books.Count ||
            !_catalog.TryGet(bookId, out var catalogItem))
        {
            return;
        }

        var book = _books[position];
        if (!string.Equals(book.BookId, bookId, StringComparison.Ordinal))
        {
            // A batched projection temporarily exposes the new collection contents
            // while the old sparse index is still published. Never apply a live
            // decoration to a card that merely occupies the old position; the
            // projection commit will reconcile the latest snapshot afterward.
            return;
        }

        var progress = _playbackDecorations.TryGetValue(bookId, out var decoration)
            ? decoration
            : EffectiveReadingProgressProjector.Project(catalogItem.Summary, _lastPlaybackSnapshot);
        var updatedBook = book.WithEffectiveProgress(
            progress,
            BuildRemainingChapterText(catalogItem.Summary.TotalChapterCount, progress.RemainingChapterCount));
        if (book.HasSameEffectiveProgress(progress, updatedBook.RemainingChapterText))
        {
            return;
        }

        _books.ReplaceAt(position, updatedBook);
        if (_visibleBookProjection is LibraryBookCardProjection[] projection)
        {
            projection[position] = updatedBook;
        }

        if (_visibleBookRowPositions.TryGetValue(
                bookId,
                out var rowPosition) &&
            (uint)rowPosition.RowIndex < (uint)_rows.Count &&
            (uint)rowPosition.ColumnIndex < (uint)_rows[rowPosition.RowIndex].Cards.Count &&
            string.Equals(
                _rows[rowPosition.RowIndex].Cards[rowPosition.ColumnIndex].Book.BookId,
                bookId,
                StringComparison.Ordinal))
        {
            _rows[rowPosition.RowIndex].UpdateBook(rowPosition.ColumnIndex, updatedBook);
        }
    }

    private bool IsCurrentPlaybackBook(string bookId)
    {
        return string.Equals(_playbackCoordinator.CurrentSnapshot.BookId, bookId, StringComparison.Ordinal);
    }

    private static string BuildRemainingChapterText(int totalChapterCount, int remainingChapterCount)
    {
        return totalChapterCount > 0 && remainingChapterCount <= 0
            ? "最后一章"
            : $"剩余 {Math.Max(0, remainingChapterCount)} 章";
    }

    private static string BuildLibrarySummary(int totalBooks, LibrarySortMode sortMode)
    {
        return sortMode == LibrarySortMode.Title
            ? $"共 {totalBooks} 本 · 按书名排序"
            : $"共 {totalBooks} 本 · 最近阅读优先";
    }

    private void InvalidateVisibleProjection()
    {
        Interlocked.Increment(ref _projectionVersion);
        CancelActiveProjection();
        CancelActiveRowLayout();
    }

    private void RebuildVisibleBookPositions()
    {
        var positions = new Dictionary<string, int>(_books.Count, StringComparer.Ordinal);
        for (var index = 0; index < _books.Count; index++)
        {
            positions[_books[index].BookId] = index;
        }

        _visibleBookPositions = positions;
        OnPropertyChanged(nameof(VisibleBookPositions));
    }

    private async Task RebuildRowsForCurrentProjectionAsync(CancellationToken cancellationToken)
    {
        CancelActiveRowLayout();
        var projection = _visibleBookProjection.ToArray();
        var projectionVersion = Volatile.Read(ref _projectionVersion);
        var playbackSnapshotVersion = Volatile.Read(ref _playbackSnapshotVersion);
        var availableWidth = _availableWidth;
        var layout = await CreateRowsAsync(projection, availableWidth, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (projectionVersion != Volatile.Read(ref _projectionVersion) ||
            playbackSnapshotVersion != Volatile.Read(ref _playbackSnapshotVersion) ||
            Math.Abs(availableWidth - _availableWidth) >= 0.1d)
        {
            ScheduleRowsRebuild();
            return;
        }

        ApplyRowsLayout(layout);
    }

    private void ScheduleRowsRebuild()
    {
        var version = Interlocked.Increment(ref _rowLayoutVersion);
        CancelActiveRowLayout();
        var projection = _visibleBookProjection.ToArray();
        var projectionVersion = Volatile.Read(ref _projectionVersion);
        var playbackSnapshotVersion = Volatile.Read(ref _playbackSnapshotVersion);
        var availableWidth = _availableWidth;
        if (projection.Length < BackgroundRowLayoutThreshold)
        {
            ApplyRowsLayout(LibraryResponsiveLayout.Create(projection, availableWidth));
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        _activeRowLayoutCancellationTokenSource = cancellationTokenSource;
        _pageTasks.Register(
            RebuildRowsInBackgroundAsync(
                version,
                projection,
                projectionVersion,
                playbackSnapshotVersion,
                availableWidth,
                cancellationTokenSource),
            exception => _feedbackService.ShowProjectedNotification(
                "更新书库布局失败",
                _feedbackService.Project(exception)));
    }

    private async Task RebuildRowsInBackgroundAsync(
        int version,
        IReadOnlyList<LibraryBookCardProjection> projection,
        int projectionVersion,
        int playbackSnapshotVersion,
        double availableWidth,
        CancellationTokenSource cancellationTokenSource)
    {
        var reschedule = false;
        try
        {
            var layout = await Task.Run(
                () => LibraryResponsiveLayout.Create(
                    projection,
                    availableWidth,
                    cancellationToken: cancellationTokenSource.Token),
                cancellationTokenSource.Token).ConfigureAwait(true);
            cancellationTokenSource.Token.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _rowLayoutVersion) ||
                projectionVersion != Volatile.Read(ref _projectionVersion) ||
                Math.Abs(availableWidth - _availableWidth) >= 0.1d)
            {
                return;
            }

            if (playbackSnapshotVersion != Volatile.Read(ref _playbackSnapshotVersion))
            {
                reschedule = true;
                return;
            }

            ApplyRowsLayout(layout);
        }
        finally
        {
            if (ReferenceEquals(_activeRowLayoutCancellationTokenSource, cancellationTokenSource))
            {
                _activeRowLayoutCancellationTokenSource = null;
                if (reschedule)
                {
                    ScheduleRowsRebuild();
                }
            }

            cancellationTokenSource.Dispose();
        }
    }

    private static async Task<LibraryResponsiveLayoutResult> CreateRowsAsync(
        IReadOnlyList<LibraryBookCardProjection> projection,
        double availableWidth,
        CancellationToken cancellationToken)
    {
        if (projection.Count < BackgroundRowLayoutThreshold)
        {
            return LibraryResponsiveLayout.Create(
                projection,
                availableWidth,
                cancellationToken: cancellationToken);
        }

        return await Task.Run(
            () => LibraryResponsiveLayout.Create(
                projection,
                availableWidth,
                cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(true);
    }

    private void ApplyRowsLayout(LibraryResponsiveLayoutResult layout)
    {
        _visibleBookRowPositions = layout.BookPositions;
        OnPropertyChanged(nameof(VisibleBookRowPositions));

        if (ColumnCount != layout.ColumnCount)
        {
            ColumnCount = layout.ColumnCount;
            OnPropertyChanged(nameof(ColumnCount));
        }

        if (Math.Abs(CardWidth - layout.CardWidth) >= 0.1d)
        {
            CardWidth = layout.CardWidth;
            OnPropertyChanged(nameof(CardWidth));
        }

        _rows.ReplaceWith(layout.Rows);
    }

    private void CancelActiveRowLayout()
    {
        _activeRowLayoutCancellationTokenSource?.Cancel();
    }

    private void CancelActiveProjection()
    {
        _activeProjectionCancellationTokenSource?.Cancel();
    }

    private void ReplaceActiveImport(CancellationToken cancellationToken)
    {
        _activeImportCancellationTokenSource?.Cancel();
        _activeImportCancellationTokenSource?.Dispose();
        _activeImportCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    private bool IsCurrentImport(int version, CancellationTokenSource activeCancellationTokenSource)
    {
        return version == Volatile.Read(ref _importVersion) &&
            ReferenceEquals(_activeImportCancellationTokenSource, activeCancellationTokenSource) &&
            !activeCancellationTokenSource.IsCancellationRequested;
    }

    private bool IsCurrentLoad(int version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return version == Volatile.Read(ref _loadVersion);
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
