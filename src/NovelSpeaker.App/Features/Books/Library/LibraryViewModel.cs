using NovelSpeaker.App.Shell.Activation;
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
using NovelSpeaker.App.Shared.Presentation.Selection;

namespace NovelSpeaker.App.Features.Books.Library;

/// <summary>
/// Owns page state and commands while the library catalog and card projection remain
/// separate lifetimes.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject, ITransientEscapeHandler
{
    private const int BackgroundRowLayoutThreshold = 512;

    private static readonly IReadOnlyList<LibrarySortOption> SortOptions =
    [
        new(LibrarySortMode.RecentReading, "最近阅读"),
        new(LibrarySortMode.Title, "书名"),
        new(LibrarySortMode.RecentImport, "最近导入")
    ];

    private readonly IBookLibraryQuery _bookLibraryQuery;
    private readonly IBookDeletionService _bookDeletionService;
    private readonly ILibraryImportCoordinator _libraryImportCoordinator;
    private readonly IBookDeleteDialogService _deleteDialogService;
    private readonly IBookSourceChangeSource _bookChanges;
    private PageActivationScope? _bookChangeActivation;
    private readonly SemaphoreSlim _catalogUpdates = new(1, 1);
    private Task<bool>? _criticalLoadTask;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppNavigator _navigator;
    private readonly IPlaybackSnapshotSource _playbackCoordinator;
    private readonly IUiScheduler _uiScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly IBookCoverGenerator _bookCoverGenerator;
    private readonly ManagementSelectionController<string> _selection = new(StringComparer.Ordinal);
    private readonly IBookTextExportService? _textExportService;
    private readonly IPresentationFileDialogService? _fileDialogs;
    private CancellationTokenSource _managementLifetime = new();
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
    private readonly LatestOperationSlot _searchOperation = new();
    private CancellationTokenSource? _activeProjectionCancellationTokenSource;
    private CancellationTokenSource? _activeRowLayoutCancellationTokenSource;
    private CancellationTokenSource? _activeImportCancellationTokenSource;
    private LibraryBookCatalog _catalog;
    private PlaybackSnapshot _lastPlaybackSnapshot;
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
        IBookSourceChangeSource bookChanges,
        IAppFeedbackService feedbackService,
        IAppNavigator navigator,
        IPlaybackSnapshotSource playbackCoordinator,
        LibraryScrollState scrollState,
        IUiScheduler? uiScheduler = null,
        TimeProvider? timeProvider = null,
        IBookTextExportService? textExportService = null,
        IPresentationFileDialogService? fileDialogs = null)
    {
        _bookLibraryQuery = bookLibraryQuery;
        _bookDeletionService = bookDeletionService;
        _bookCoverGenerator = bookCoverGenerator;
        _libraryImportCoordinator = libraryImportCoordinator;
        _deleteDialogService = deleteDialogService;
        _bookChanges = bookChanges;
        _feedbackService = feedbackService;
        _navigator = navigator;
        _playbackCoordinator = playbackCoordinator;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        _timeProvider = timeProvider ?? TimeProvider.System;
        ScrollState = scrollState;
        _catalog = new LibraryBookCatalog([]);
        _lastPlaybackSnapshot = playbackCoordinator.CurrentSnapshot;
        _textExportService = textExportService;
        _fileDialogs = fileDialogs;
        _selection.StateChanged += (_, _) =>
        {
            foreach (var key in _selection.ChangedItems)
            {
                if (_visibleBookPositions.TryGetValue(key, out var position))
                    _visibleBookProjection[position].IsSelected = _selection.IsSelected(key);
            }
            OnPropertyChanged(nameof(IsManagementMode));
            OnPropertyChanged(nameof(SelectedBookCount));
        };
    }

    public bool IsManagementMode => _selection.IsManagementMode;

    public int SelectedBookCount => _selection.SelectedCount;

    public bool TryHandleEscape() => _selection.Exit();

    [RelayCommand]
    private void EnterManagement() => _selection.Enter();

    [RelayCommand]
    private void CancelManagement() => _selection.Exit();

    [RelayCommand]
    private void SelectAllBooks() => _selection.SelectAll();

    public bool HandleBookClick(LibraryBookCardProjection book, DesktopSelectionModifiers modifiers) =>
        _selection.HandleClick(book.BookId, modifiers);

    [RelayCommand]
    private void PrepareBookContext(LibraryBookCardProjection? book)
    {
        if (book is not null) _selection.HandleRightClick(book.BookId);
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

    public Task<bool> LoadAsync(CancellationToken cancellationToken) =>
        _criticalLoadTask = LoadCoreAsync(cancellationToken);

    private async Task<bool> LoadCoreAsync(CancellationToken cancellationToken)
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
        IsBusy = true;
        try
        {
            var outcome = await _libraryImportCoordinator.ImportAsync(selectedPath, activeCancellationTokenSource.Token);
            if (!IsCurrentImport(version, activeCancellationTokenSource))
            {
                return;
            }

            if (outcome.Status == LibraryImportCoordinatorStatus.Imported)
            {
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
            }
        }
    }

    public void CancelActiveImport()
    {
        _activeImportCancellationTokenSource?.Cancel();
        _activeImportCancellationTokenSource?.Dispose();
        _activeImportCancellationTokenSource = null;
        IsBusy = false;
    }

    public void HandleNavigatedTo(PageActivationScope activation)
    {
        _bookChangeActivation?.Dispose();
        _bookChangeActivation = activation;
        activation.Register(() =>
        {
            if (ReferenceEquals(_bookChangeActivation, activation)) HandleNavigatedFrom();
        });
        if (_managementLifetime.IsCancellationRequested)
        {
            _managementLifetime.Dispose();
            _managementLifetime = new CancellationTokenSource();
        }
        RegisterPageEvents();
        RebuildVisibleBookIndex();
        ReconcileSelection();
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
        var activation = _bookChangeActivation;
        _bookChangeActivation = null;
        activation?.Dispose();
        _managementLifetime.Cancel();
        _selection.Reset();
        var projectionWasActive = _activeProjectionCancellationTokenSource is not null;
        var projectionWasPending = _searchOperation.Current is not null;
        var rowLayoutWasActive = _activeRowLayoutCancellationTokenSource is not null;
        Interlocked.Increment(ref _loadVersion);
        CancelActiveImport();
        _searchOperation.Cancel();
        InvalidateVisibleProjection();
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
        _bookChanges.Changed -= OnBookCommittedChange;
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
        _bookChanges.Changed += OnBookCommittedChange;
        Interlocked.Increment(ref _playbackProjectionVersion);
        _isPageEventsRegistered = true;
    }

    private void OnBookCommittedChange(object? sender, BookCommittedChange change)
    {
        if (_bookChangeActivation is not { IsCurrent: true } activation) return;
        activation.Run(token => _uiScheduler.InvokeLaterAsync(() =>
            activation.Run(ct => RefreshBookAsync(change.BookId, ct), ReportBookRefreshFailure), token),
            ReportBookRefreshFailure);
    }

    private async Task RefreshBookAsync(string bookId, CancellationToken cancellationToken)
    {
        await _catalogUpdates.WaitAsync(cancellationToken);
        try
        {
            while (_criticalLoadTask is { } loading)
            {
                try { await loading.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                if (ReferenceEquals(loading, _criticalLoadTask)) break;
            }
            var loadVersion = Volatile.Read(ref _loadVersion);
            var summaries = await _bookLibraryQuery.GetBooksAsync([bookId], cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (loadVersion != Volatile.Read(ref _loadVersion)) return;
            LibraryBookCatalog BuildCatalog() => new(
                _catalog.Items.Where(item => item.BookId != bookId).Select(item => item.Summary)
                    .Concat(summaries).ToArray());
            var catalog = _catalog.Count >= 512
                ? await Task.Run(BuildCatalog, cancellationToken)
                : BuildCatalog();
            if (loadVersion != Volatile.Read(ref _loadVersion)) return;
            while (!await ProjectVisibleBooksAsync(cancellationToken, catalog))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (loadVersion != Volatile.Read(ref _loadVersion)) return;
            }
        }
        finally
        {
            _catalogUpdates.Release();
        }
    }

    private void ReportBookRefreshFailure(Exception exception) =>
        _feedbackService.ShowProjectedNotification("刷新书库失败", _feedbackService.Project(exception));

    [RelayCommand]
    private Task OpenBook(LibraryBookCardProjection? book, CancellationToken cancellationToken)
    {
        if (book is null)
        {
            return Task.CompletedTask;
        }

        if (HandleBookClick(book, DesktopSelectionModifiers.None)) return Task.CompletedTask;

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
        if (IsManagementMode)
        {
            await DeleteSelectedBooksAsync(cancellationToken);
            return;
        }
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
            var result = await _bookDeletionService.DeleteAsync(
                new BookDeleteRequest(book.BookId, decision.DeleteAudioCache),
                cancellationToken);

            if (result is null)
            {
                if (!await LoadAsync(cancellationToken))
                {
                    return;
                }
                _feedbackService.ShowWarning("书籍已不存在", "这本书已不存在，书库已刷新。");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            _feedbackService.ShowSuccess("删除成功", $"已删除《{book.Title}》。");
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            _feedbackService.ShowProjectedNotification("删除失败", projected);
        }
        finally
        {
            _isDeletingBook = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedBooksAsync(CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _managementLifetime.Token);
        cancellationToken = linkedCancellation.Token;
        var books = GetSelectedBooks();
        if (books.Length == 0 || _isDeletingBook) return;
        _isDeletingBook = true;
        try
        {
            var decision = await _deleteDialogService.ShowAsync(
                new BookDeleteDialogRequest($"{books.Length} 本书籍", books.Any(book => IsCurrentPlaybackBook(book.BookId)), BookCount: books.Length),
                cancellationToken);
            if (!decision.IsConfirmed) return;
            var succeeded = 0;
            var skipped = 0;
            var failed = 0;
            foreach (var book in books)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await _bookDeletionService.DeleteAsync(new BookDeleteRequest(book.BookId, decision.DeleteAudioCache), cancellationToken);
                    if (result is null) skipped++;
                    else succeeded++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { failed++; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            ShowBatchCompletion("删除完成", succeeded, skipped, failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { _feedbackService.ShowProjectedNotification("删除失败", _feedbackService.Project(exception)); }
        finally { _isDeletingBook = false; }
    }

    [RelayCommand]
    private async Task ExportBooksAsync(LibraryBookCardProjection? book, CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _managementLifetime.Token);
        cancellationToken = linkedCancellation.Token;
        var books = IsManagementMode ? GetSelectedBooks() : book is null ? [] : new[] { book };
        if (books.Length == 0 || _textExportService is null || _fileDialogs is null) return;
        try
        {
            var directory = await _fileDialogs.PickFolderAsync(new PresentationFolderDialogOptions("导出书籍正文"), cancellationToken);
            if (directory is null) return;
            var succeeded = 0;
            var skipped = 0;
            var failed = 0;
            foreach (var item in books)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await _textExportService.ExportAsync(item.BookId, directory, cancellationToken)) succeeded++;
                    else skipped++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { failed++; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            ShowBatchCompletion("导出完成", succeeded, skipped, failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { _feedbackService.ShowProjectedNotification("导出失败", _feedbackService.Project(exception)); }
    }

    private void ShowBatchCompletion(string title, int succeeded, int skipped, int failed)
    {
        var message = $"成功 {succeeded}，跳过 {skipped}，失败 {failed}。";
        if (skipped > 0 || failed > 0)
            _feedbackService.ShowWarning(title, message);
        else
            _feedbackService.ShowSuccess(title, message);
    }

    private LibraryBookCardProjection[] GetSelectedBooks() =>
        _visibleBookProjection.Where(book => _selection.IsSelected(book.BookId)).ToArray();

    private void ReconcileSelection()
    {
        _selection.SetItems(_visibleBookProjection.Select(book => book.BookId));
        foreach (var book in _visibleBookProjection) book.IsSelected = _selection.IsSelected(book.BookId);
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
        var operation = _searchOperation.Begin(activation: _bookChangeActivation);
        InvalidateVisibleProjection();
        var task = operation.RunAsync(
            async current =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(120), _timeProvider, current.CancellationToken);
                if (current.IsCurrent) await ProjectVisibleBooksAsync(current.CancellationToken);
            },
            exception => _feedbackService.ShowProjectedNotification(
                "更新书库筛选失败",
                _feedbackService.Project(exception)));
        if (_bookChangeActivation is { } activation) activation.Register(task);
        else _pageTasks.Register(task);
    }

    private void ScheduleVisibleProjection()
    {
        _searchOperation.Cancel();
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
            ReconcileSelection();
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
        return sortMode switch
        {
            LibrarySortMode.Title => $"共 {totalBooks} 本 · 按书名排序",
            LibrarySortMode.RecentImport => $"共 {totalBooks} 本 · 最近导入优先",
            _ => $"共 {totalBooks} 本 · 最近阅读优先"
        };
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

    private void ShowImportFailure(BookImportFailureReason? failureReason)
    {
        var message = failureReason switch
        {
            BookImportFailureReason.NoValidChapters => "章节解析失败，请检查文件内容。",
            BookImportFailureReason.UnsupportedEncoding => "无法识别编码，请手动选择。",
            BookImportFailureReason.FileReadFailed => "文件无法读取，请确认文件仍可访问。",
            BookImportFailureReason.ChapterRuleTimedOut => "章节规则匹配超时，请调整规则后重试。",
            _ => "导入失败，请重试。"
        };

        _feedbackService.ShowWarning("无法导入", message);
    }
}
