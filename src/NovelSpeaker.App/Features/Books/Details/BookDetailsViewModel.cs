using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Features.Books.Shared;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Cache;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.Books.Details;

public sealed partial class BookDetailsViewModel : ObservableObject
{
    private const int CacheDecorationWindowSize = 32;
    private readonly IBookDetailsQuery _bookDetailsQuery;
    private readonly IBookMetadataUpdateService _bookMetadataUpdateService;
    private readonly IBookDeletionService _bookDeletionService;
    private readonly ICacheWorkspaceService _cacheWorkspaceService;
    private readonly IAppSettingsService _settingsService;
    private readonly IUiScheduler _uiScheduler;
    private readonly IBookCoverGenerator _bookCoverGenerator;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly IBookDeleteDialogService _deleteDialogService;
    private readonly IBookCatalogInvalidationState _catalogInvalidationState;
    private readonly IAppNavigator _navigator;
    private readonly IPlaybackBookCommands _playbackCoordinator;
    private readonly ChapterCacheStatusRefreshController _cacheStatusRefresh;
    private readonly OwnedTaskRegistry _pageTasks = new();
    private CancellationTokenSource? _activeLoadCancellationTokenSource;
    private int _loadVersion;
    private BookDetailsHeader? _loadedHeader;
    private IReadOnlyList<BookChapterSummary> _loadedCatalog = [];
    private BookReadingPosition? _loadedReadingPosition;
    private BookDetailsStatistics? _loadedStatistics;
    private string? _bookId;
    private CancellationTokenSource? _cacheStatusCancellationTokenSource;
    private bool _isCacheStatusUpdatesActive;
    private bool _deferInitialCacheStatusProjection;
    private bool _initialCacheStatusProjectionPending;
    private bool _isPlaybackEventsRegistered;
    private int _playbackProjectionVersion;

    public BookDetailsViewModel(
        IBookDetailsQuery bookDetailsQuery,
        IBookMetadataUpdateService bookMetadataUpdateService,
        IBookDeletionService bookDeletionService,
        ICacheWorkspaceService cacheWorkspaceService,
        IAppSettingsService settingsService,
        IBookCoverGenerator bookCoverGenerator,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        IBookDeleteDialogService deleteDialogService,
        IBookCatalogInvalidationState catalogInvalidationState,
        IPlaybackBookCommands playbackCoordinator,
        IAppNavigator navigator,
        IUiScheduler? uiScheduler = null)
    {
        _bookDetailsQuery = bookDetailsQuery;
        _bookMetadataUpdateService = bookMetadataUpdateService;
        _bookDeletionService = bookDeletionService;
        _cacheWorkspaceService = cacheWorkspaceService;
        _settingsService = settingsService;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        _bookCoverGenerator = bookCoverGenerator;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _deleteDialogService = deleteDialogService;
        _catalogInvalidationState = catalogInvalidationState;
        _playbackCoordinator = playbackCoordinator;
        _navigator = navigator;
        _cacheStatusRefresh = new ChapterCacheStatusRefreshController(
            _cacheWorkspaceService,
            _uiScheduler,
            ApplyChapterCacheStatuses,
            ReportCacheStatusRefreshFailure);
        Cover = _bookCoverGenerator.Generate("未命名书籍");
    }

    private readonly ResettableObservableCollection<BookDetailsChapterItemViewModel> _chapters = [];
    private IndexedCatalog<BookChapterSummary> _chapterCatalog =
        new([], static chapter => chapter.ChapterIndex);
    private readonly SparseCatalogDecoration<bool> _currentChapterDecoration = new();
    private readonly SparseCatalogDecoration<string> _cacheDecorations = new();
    private readonly HashSet<int> _cacheDecorationWindow = [];
    private readonly HashSet<int> _explicitCacheStatusRequests = [];
    private BookDetailsChapterItemViewModel? _currentChapterItem;

    public ObservableCollection<BookDetailsChapterItemViewModel> Chapters => _chapters;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasBook;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string editTitle = string.Empty;

    [ObservableProperty]
    private string editAuthor = string.Empty;

    [ObservableProperty]
    private string displayAuthor = "未知作者";

    [ObservableProperty]
    private string totalChapterCountText = string.Empty;

    [ObservableProperty]
    private string currentChapterText = "未开始";

    [ObservableProperty]
    private string chapterCatalogSummaryText = string.Empty;

    [ObservableProperty]
    private double progressRatio;

    [ObservableProperty]
    private string progressText = "0%";

    [ObservableProperty]
    private string cacheSizeText = "0 B";

    [ObservableProperty]
    private GeneratedBookCover cover;

    public BookDetailsChapterItemViewModel? CurrentChapterItem => _currentChapterItem;

    public bool HasUnsavedChanges => _loadedHeader is not null &&
        (!string.Equals(_loadedHeader.Title, NormalizeTitle(EditTitle), StringComparison.Ordinal) ||
         !string.Equals(NormalizeAuthor(_loadedHeader.Author), NormalizeAuthor(EditAuthor), StringComparison.Ordinal));

    public bool CanSave => _loadedHeader is not null &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(NormalizeTitle(EditTitle)) &&
        HasUnsavedChanges;

    public bool CanCancelEdit => _loadedHeader is not null && !IsBusy && HasUnsavedChanges;

    public bool CanClearCache => _loadedHeader is not null &&
        !string.IsNullOrWhiteSpace(_bookId) &&
        !IsBusy;

    internal void DeferInitialCacheStatusProjection()
    {
        _deferInitialCacheStatusProjection = true;
    }

    internal void NotifyInitialChapterLocatorCompleted()
    {
        if (!_deferInitialCacheStatusProjection || !_initialCacheStatusProjectionPending)
        {
            return;
        }

        _deferInitialCacheStatusProjection = false;
        _initialCacheStatusProjectionPending = false;
        QueueCacheStatusRefresh(chapterIndex: null, isInitialProjection: true);
    }

    internal bool HasInitialCacheStatusProjectionPending =>
        _deferInitialCacheStatusProjection && _initialCacheStatusProjectionPending;

    internal void RequestCacheDecorationWindow(int start, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var cancellationToken = _cacheStatusCancellationTokenSource?.Token ??
            new CancellationToken(canceled: true);
        void Request()
        {
            if (!_isCacheStatusUpdatesActive ||
                _cacheStatusCancellationTokenSource is not { IsCancellationRequested: false } ||
                string.IsNullOrWhiteSpace(_bookId) ||
                _chapterCatalog.Count == 0)
            {
                return;
            }

            var chapterIndices = _chapterCatalog
                .Slice(Math.Clamp(start, 0, _chapterCatalog.Count - 1), count)
                .Select(static chapter => chapter.ChapterIndex)
                .ToArray();
            if (chapterIndices.Length == 0)
            {
                return;
            }

            SetCacheDecorationWindow(chapterIndices);
            ClearStaleCacheDecorations(chapterIndices);
            _cacheStatusRefresh.Request(_bookId!, chapterIndices);
        }

        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(Request, cancellationToken),
                ReportCacheStatusRefreshFailure);
            return;
        }

        Request();
    }

    public async Task LoadAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        var version = Interlocked.Increment(ref _loadVersion);
        CancelPendingLoad();
        _bookId = bookId;
        ActivateCacheStatusUpdates(cancellationToken);
        IsBusy = true;
        StatusMessage = string.Empty;
        ResetDetailSupplementProjection();

        try
        {
            var header = await _bookDetailsQuery.GetHeaderAsync(bookId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _loadVersion))
            {
                return;
            }

            if (header is null)
            {
                ClearBook();
                StatusMessage = "未找到这本书，可能已经被删除。";
                IsBusy = false;
                NotifyCommandStateChanged();
                return;
            }

            ApplyHeader(header);
            BeginLoadDetailsSupplement(bookId, version, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _loadVersion))
            {
                IsBusy = false;
                NotifyCommandStateChanged();
            }

            throw;
        }
        catch (Exception exception)
        {
            if (version != Volatile.Read(ref _loadVersion))
            {
                return;
            }

            var projected = _feedbackService.Project(exception);
            ClearBook();
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("加载书籍详情失败", projected);
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    public void HandleNavigatedFrom()
    {
        Interlocked.Increment(ref _loadVersion);
        CancelPendingLoad();
        DeactivateCacheStatusUpdates();
        _deferInitialCacheStatusProjection = false;
        _initialCacheStatusProjectionPending = false;
        if (_isPlaybackEventsRegistered)
        {
            _playbackCoordinator.SnapshotChanged -= OnPlaybackSnapshotChanged;
            Interlocked.Increment(ref _playbackProjectionVersion);
            _isPlaybackEventsRegistered = false;
        }

        IsBusy = false;
        NotifyCommandStateChanged();
    }

    public void HandleNavigatedTo()
    {
        RegisterPlaybackEvents();
        ApplyPlaybackSnapshot(_playbackCoordinator.CurrentSnapshot);
    }

    [RelayCommand]
    private Task BackAsync(CancellationToken cancellationToken)
    {
        return RequestNavigateBackAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_loadedHeader is null || string.IsNullOrWhiteSpace(_bookId))
        {
            return;
        }

        await SaveCoreAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanCancelEdit))]
    private void CancelEdit()
    {
        if (_loadedHeader is null)
        {
            return;
        }

        EditTitle = _loadedHeader.Title;
        EditAuthor = _loadedHeader.Author ?? string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanClearCache), AllowConcurrentExecutions = false)]
    private async Task ClearCacheAsync(CancellationToken cancellationToken)
    {
        if (_loadedHeader is null || string.IsNullOrWhiteSpace(_bookId) || IsBusy)
        {
            return;
        }

        if (!await ConfirmLeaveAsync(cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        var decision = await _dialogService.ShowConfirmationAsync(
            "清理缓存",
            "将清理这本书的音频缓存，不会删除书籍、阅读进度或内部 TXT。",
            "清理",
            "取消",
            cancellationToken);
        if (decision != AppConfirmationDecision.Confirm)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _cacheWorkspaceService.ClearBookAsync(_bookId, cancellationToken);
            var statistics = await _bookDetailsQuery.GetStatisticsAsync(_bookId, cancellationToken);
            if (statistics is not null)
            {
                _loadedStatistics = statistics;
                CacheSizeText = FormatBytes(statistics.CachedAudioBytes);
            }

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
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("清理失败", projected);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    [RelayCommand]
    private async Task DeleteBookAsync(CancellationToken cancellationToken)
    {
        if (_loadedHeader is null || string.IsNullOrWhiteSpace(_bookId) || IsBusy)
        {
            return;
        }

        if (!await ConfirmLeaveAsync(cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        var deleteDecision = await _deleteDialogService.ShowAsync(
            new BookDeleteDialogRequest(
                _loadedHeader.Title,
                IsCurrentPlaybackBook(_bookId)),
            cancellationToken);
        if (!deleteDecision.IsConfirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (IsCurrentPlaybackBook(_bookId))
            {
                await _playbackCoordinator.HandleBookDeletedAsync(_bookId, cancellationToken);
            }

            var result = await _bookDeletionService.DeleteAsync(
                new BookDeleteRequest(_bookId, deleteDecision.DeleteAudioCache),
                cancellationToken);
            if (result is null)
            {
                StatusMessage = "这本书已不存在。";
                _catalogInvalidationState.Invalidate();
                await _navigator.NavigateBackAsync(cancellationToken, bypassGuard: true).ConfigureAwait(true);
                return;
            }

            _catalogInvalidationState.Invalidate();
            _feedbackService.ShowSuccess("删除成功", $"已删除《{_loadedHeader.Title}》。");
            await _navigator.NavigateBackAsync(cancellationToken, bypassGuard: true).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("删除书籍失败", projected);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    public async Task RequestNavigateBackAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmLeaveAsync(cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        await _navigator.NavigateBackAsync(cancellationToken, bypassGuard: true).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SelectChapterAsync(BookDetailsChapterItemViewModel? chapter, CancellationToken cancellationToken)
    {
        if (chapter is null || string.IsNullOrWhiteSpace(_bookId) || IsBusy)
        {
            return;
        }

        if (!await ConfirmLeaveAsync(cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        await _navigator.NavigateAsync(
            new PlayerRoute(
                _bookId,
                new BookDetailsRoute(_bookId),
                PlayerNavigationMode.OpenPaused,
                chapter.ChapterIndex,
                0),
            cancellationToken,
            bypassGuard: true).ConfigureAwait(true);
    }

    public async Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken)
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var decision = await _dialogService.ShowUnsavedChangesAsync(
            "未保存的修改",
            "书名或作者尚未保存。要先保存再继续当前操作吗？",
            "保存",
            "放弃",
            "取消",
            cancellationToken).ConfigureAwait(true);

        return decision switch
        {
            UnsavedChangesDecision.Save => await SaveCoreAsync(cancellationToken).ConfigureAwait(true),
            UnsavedChangesDecision.Discard => DiscardChangesAndContinue(),
            _ => false
        };
    }

    partial void OnEditTitleChanged(string value)
    {
        NotifyCommandStateChanged();
    }

    partial void OnEditAuthorChanged(string value)
    {
        NotifyCommandStateChanged();
    }

    private async Task<bool> SaveCoreAsync(CancellationToken cancellationToken)
    {
        if (_loadedHeader is null || string.IsNullOrWhiteSpace(_bookId))
        {
            return false;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        NotifyCommandStateChanged();

        try
        {
            var updated = await _bookMetadataUpdateService.UpdateMetadataAsync(
                new BookMetadataUpdateRequest(
                    _bookId,
                    NormalizeTitle(EditTitle),
                    NormalizeAuthor(EditAuthor)),
                cancellationToken);
            ApplyHeader(updated);
            _catalogInvalidationState.Invalidate();
            await _playbackCoordinator.RefreshBookMetadataAsync(_bookId, cancellationToken);
            _feedbackService.ShowSuccess("已保存", "书名和作者已更新。");
            return true;
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("保存书籍信息失败", projected);
            return false;
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    private void BeginLoadDetailsSupplement(
        string bookId,
        int loadVersion,
        CancellationToken cancellationToken)
    {
        var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeLoadCancellationTokenSource = linkedCancellationTokenSource;
        _pageTasks.Register(LoadDetailsSupplementAsync(bookId, loadVersion, linkedCancellationTokenSource));
    }

    private async Task LoadDetailsSupplementAsync(
        string bookId,
        int loadVersion,
        CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            // Let the page publish its header and first frame before starting the large
            // supplement projection. The WPF scheduler posts this continuation at background
            // priority; test schedulers may intentionally execute the no-op inline.
            await _uiScheduler.InvokeLaterAsync(static () => { }, cancellationTokenSource.Token);

            var catalogTask = _bookDetailsQuery.GetCatalogAsync(bookId, cancellationTokenSource.Token);
            var readingPositionTask = _bookDetailsQuery.GetReadingPositionAsync(bookId, cancellationTokenSource.Token);
            var statisticsTask = _bookDetailsQuery.GetStatisticsAsync(bookId, cancellationTokenSource.Token);
            await Task.WhenAll(catalogTask, readingPositionTask, statisticsTask).ConfigureAwait(true);
            if (!IsCurrentLoad(loadVersion, cancellationTokenSource))
            {
                return;
            }

            var statistics = await statisticsTask.ConfigureAwait(true);
            if (statistics is null)
            {
                ClearBook();
                StatusMessage = "未找到这本书，可能已经被删除。";
                return;
            }

            await ApplyDetailsAsync(
                await catalogTask.ConfigureAwait(true),
                await readingPositionTask.ConfigureAwait(true),
                statistics,
                loadVersion,
                cancellationTokenSource);
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_activeLoadCancellationTokenSource, cancellationTokenSource))
            {
                return;
            }

            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("加载书籍详情失败", projected);
        }
        finally
        {
            cancellationTokenSource.Dispose();
            if (ReferenceEquals(_activeLoadCancellationTokenSource, cancellationTokenSource))
            {
                _activeLoadCancellationTokenSource = null;
                IsBusy = false;
                NotifyCommandStateChanged();
            }
        }
    }

    private bool IsCurrentLoad(int loadVersion, CancellationTokenSource cancellationTokenSource) =>
        loadVersion == Volatile.Read(ref _loadVersion) &&
        ReferenceEquals(_activeLoadCancellationTokenSource, cancellationTokenSource) &&
        !cancellationTokenSource.IsCancellationRequested;

    private void ApplyHeader(BookDetailsHeader header, bool preserveEditor = false)
    {
        var hadUnsavedChanges = HasUnsavedChanges;
        _loadedHeader = header;
        HasBook = true;
        Title = header.Title;
        DisplayAuthor = string.IsNullOrWhiteSpace(header.Author) ? "未知作者" : header.Author.Trim();
        Cover = _bookCoverGenerator.Generate(header.Title);

        if (!preserveEditor || !hadUnsavedChanges)
        {
            EditTitle = header.Title;
            EditAuthor = header.Author ?? string.Empty;
        }

        NotifyCommandStateChanged();
    }

    private async Task ApplyDetailsAsync(
        IReadOnlyList<BookChapterSummary> catalog,
        BookReadingPosition? readingPosition,
        BookDetailsStatistics statistics,
        int loadVersion,
        CancellationTokenSource loadCancellation)
    {
        var chapterCatalog = await Task.Run(
            () => new IndexedCatalog<BookChapterSummary>(
                catalog,
                static chapter => chapter.ChapterIndex),
            loadCancellation.Token).ConfigureAwait(true);
        if (!IsCurrentLoad(loadVersion, loadCancellation))
        {
            return;
        }

        var initialSnapshot = _playbackCoordinator.CurrentSnapshot;
        var progress = EffectiveReadingProgressProjector.Project(
            _loadedHeader!.Id,
            catalog,
            readingPosition,
            initialSnapshot,
            chapterIndex => chapterCatalog.TryGetPosition(chapterIndex, out var position)
                ? position
                : null);

        _loadedCatalog = catalog;
        _loadedReadingPosition = readingPosition;
        _loadedStatistics = statistics;
        _chapterCatalog = chapterCatalog;
        _currentChapterDecoration.Clear();
        _cacheDecorations.Clear();
        _cacheDecorationWindow.Clear();
        _explicitCacheStatusRequests.Clear();
        _currentChapterItem = null;
        TotalChapterCountText = $"共 {catalog.Count} 章";
        CacheSizeText = FormatBytes(statistics.CachedAudioBytes);
        Cover = _bookCoverGenerator.Generate(Title);

        if (progress.CurrentChapterIndex is int currentChapterIndex)
        {
            _currentChapterDecoration.Set(currentChapterIndex, true);
        }

        _initialCacheStatusProjectionPending = true;
        var currentDecoration = _currentChapterDecoration.Snapshot();
        var cacheDecoration = _cacheDecorations.Snapshot();
        await _chapters.ReplaceWithInBatchesAsync(
            catalog,
            chapter => CreateChapterItem(chapter, currentDecoration, cacheDecoration),
            _uiScheduler,
            loadCancellation.Token);
        if (!IsCurrentLoad(loadVersion, loadCancellation))
        {
            return;
        }

        var latestSnapshot = _playbackCoordinator.CurrentSnapshot;
        var latestProgress = EffectiveReadingProgressProjector.Project(
            _loadedHeader.Id,
            catalog,
            readingPosition,
            latestSnapshot,
            GetChapterPosition);
        if (progress.CurrentChapterIndex != latestProgress.CurrentChapterIndex)
        {
            _currentChapterDecoration.Clear();
            if (latestProgress.CurrentChapterIndex is int latestChapterIndex)
            {
                _currentChapterDecoration.Set(latestChapterIndex, true);
            }

            var latestDecoration = _currentChapterDecoration.Snapshot();
            var replacements = new List<(int Index, BookDetailsChapterItemViewModel Item)>();
            foreach (var chapterIndex in new[] { progress.CurrentChapterIndex, latestProgress.CurrentChapterIndex })
            {
                if (chapterIndex is int index &&
                    chapterCatalog.TryGetPosition(index, out var position))
                {
                    replacements.Add((position, CreateChapterItem(
                        chapterCatalog[position],
                        latestDecoration,
                        _cacheDecorations.Snapshot())));
                }
            }

            _chapters.ReplaceAtMany(replacements.DistinctBy(static replacement => replacement.Index).ToArray());
        }

        ApplyReadingProgress(latestProgress, notify: false, updateItemState: false);
        if (!_deferInitialCacheStatusProjection && _initialCacheStatusProjectionPending)
        {
            QueueCacheStatusRefresh(chapterIndex: null, isInitialProjection: true);
        }

        StatusMessage = string.Empty;
        NotifyCommandStateChanged();
    }

    private void ClearBook()
    {
        CancelPendingLoad();
        _loadedHeader = null;
        _loadedCatalog = [];
        _loadedReadingPosition = null;
        _loadedStatistics = null;
        IsBusy = false;
        HasBook = false;
        Title = string.Empty;
        EditTitle = string.Empty;
        EditAuthor = string.Empty;
        DisplayAuthor = "未知作者";
        TotalChapterCountText = string.Empty;
        CurrentChapterText = "未开始";
        ChapterCatalogSummaryText = string.Empty;
        ProgressRatio = 0;
        ProgressText = "0%";
        CacheSizeText = "0 B";
        Cover = _bookCoverGenerator.Generate("未命名书籍");
        Chapters.Clear();
        _initialCacheStatusProjectionPending = false;
        _chapterCatalog = new IndexedCatalog<BookChapterSummary>(
            [],
            static chapter => chapter.ChapterIndex);
        _currentChapterDecoration.Clear();
        _cacheDecorations.Clear();
        _cacheDecorationWindow.Clear();
        _explicitCacheStatusRequests.Clear();
        _currentChapterItem = null;
        OnPropertyChanged(nameof(CurrentChapterItem));
        NotifyCommandStateChanged();
    }

    private void CancelPendingLoad()
    {
        _activeLoadCancellationTokenSource?.Cancel();
        _activeLoadCancellationTokenSource?.Dispose();
        _activeLoadCancellationTokenSource = null;
    }

    private void ActivateCacheStatusUpdates(CancellationToken cancellationToken)
    {
        DeactivateCacheStatusUpdates();
        _cacheStatusCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cacheStatusRefresh.Activate(_cacheStatusCancellationTokenSource.Token);
        _cacheWorkspaceService.Changed += OnCacheChanged;
        _settingsService.Changed += OnSettingsChanged;
        _isCacheStatusUpdatesActive = true;
    }

    private void DeactivateCacheStatusUpdates()
    {
        if (_isCacheStatusUpdatesActive)
        {
            _cacheWorkspaceService.Changed -= OnCacheChanged;
            _settingsService.Changed -= OnSettingsChanged;
            _isCacheStatusUpdatesActive = false;
        }

        _cacheStatusCancellationTokenSource?.Cancel();
        _cacheStatusCancellationTokenSource?.Dispose();
        _cacheStatusCancellationTokenSource = null;
        _cacheStatusRefresh.Deactivate();
    }

    private void OnCacheChanged(object? sender, CacheChangedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(_bookId) ||
            (!string.IsNullOrWhiteSpace(eventArgs.BookId) &&
             !string.Equals(eventArgs.BookId, _bookId, StringComparison.Ordinal)))
        {
            return;
        }

        ScheduleCacheStatusRefresh(eventArgs.ChapterIndex);
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs eventArgs)
    {
        if (eventArgs.Previous.DefaultSpeakSpeed == eventArgs.Current.DefaultSpeakSpeed &&
            eventArgs.Previous.SelectedTtsRuleId == eventArgs.Current.SelectedTtsRuleId &&
            eventArgs.Previous.EnableLongParagraphSplitting == eventArgs.Current.EnableLongParagraphSplitting &&
            eventArgs.Previous.LongParagraphThreshold == eventArgs.Current.LongParagraphThreshold &&
            eventArgs.Previous.ReadChapterTitle == eventArgs.Current.ReadChapterTitle)
        {
            return;
        }

        ScheduleCacheStatusRefresh(chapterIndex: null);
    }

    private void ScheduleCacheStatusRefresh(int? chapterIndex)
    {
        var cancellationToken = _cacheStatusCancellationTokenSource?.Token ?? new CancellationToken(canceled: true);
        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(() => QueueCacheStatusRefresh(chapterIndex), cancellationToken),
                ReportCacheStatusRefreshFailure);
            return;
        }

        QueueCacheStatusRefresh(chapterIndex);
    }

    private void QueueCacheStatusRefresh(int? chapterIndex, bool isInitialProjection = false)
    {
        if (!_isCacheStatusUpdatesActive ||
            _cacheStatusCancellationTokenSource is not { IsCancellationRequested: false } ||
            string.IsNullOrWhiteSpace(_bookId))
        {
            return;
        }

        var bookId = _bookId;
        var chapterIndices = chapterIndex is null
            ? GetCacheDecorationWindow()
            : _chapterCatalog.TryGet(chapterIndex.Value, out _)
                ? new[] { chapterIndex.Value }
                : Array.Empty<int>();

        if (chapterIndex is null)
        {
            SetCacheDecorationWindow(chapterIndices);
            ClearStaleCacheDecorations(chapterIndices);
        }
        else if (chapterIndices.Count > 0)
        {
            _explicitCacheStatusRequests.Add(chapterIndex.Value);
        }

        _cacheStatusRefresh.Request(bookId, chapterIndices.ToArray(), isInitialProjection);
    }

    private void ClearStaleCacheDecorations(IReadOnlyCollection<int> requestedChapterIndices)
    {
        var requested = requestedChapterIndices.ToHashSet();
        foreach (var chapterIndex in _cacheDecorations.Snapshot().Keys)
        {
            if (requested.Contains(chapterIndex))
            {
                continue;
            }

            _cacheDecorations.Remove(chapterIndex);
            if (_chapterCatalog.TryGetPosition(chapterIndex, out var position))
            {
                ReplaceChapterItem(position, CreateChapterItem(_chapterCatalog[position]));
            }
        }
    }

    private void SetCacheDecorationWindow(IReadOnlyCollection<int> chapterIndices)
    {
        _cacheDecorationWindow.Clear();
        _cacheDecorationWindow.UnionWith(chapterIndices);
    }

    private IReadOnlyList<int> GetCacheDecorationWindow()
    {
        if (_chapterCatalog.Count == 0)
        {
            return [];
        }

        var playbackSnapshot = _playbackCoordinator.CurrentSnapshot;
        var currentChapterIndex = string.Equals(playbackSnapshot.BookId, _bookId, StringComparison.Ordinal) &&
                                  playbackSnapshot.ChapterIndex >= 0
            ? playbackSnapshot.ChapterIndex
            : _loadedReadingPosition?.ChapterIndex;
        if (currentChapterIndex is null || !_chapterCatalog.TryGetPosition(currentChapterIndex.Value, out var currentPosition))
        {
            return _chapterCatalog.Slice(0, CacheDecorationWindowSize)
                .Select(static chapter => chapter.ChapterIndex)
                .ToArray();
        }

        var start = Math.Max(0, currentPosition - (CacheDecorationWindowSize / 4));
        return _chapterCatalog.Slice(start, CacheDecorationWindowSize)
            .Select(static chapter => chapter.ChapterIndex)
            .ToArray();
    }

    private void ApplyChapterCacheStatuses(
        string bookId,
        IReadOnlyCollection<int> requestedChapterIndices,
        IReadOnlyCollection<ChapterCacheStatus> statuses,
        bool isInitialProjection)
    {
        if (!_isCacheStatusUpdatesActive ||
            !string.Equals(_bookId, bookId, StringComparison.Ordinal))
        {
            return;
        }

        var statusesByChapter = statuses.ToDictionary(static status => status.ChapterIndex);
        var changed = false;
        var previousCurrentItem = _currentChapterItem;
        foreach (var chapterIndex in requestedChapterIndices)
        {
            if (!_cacheDecorationWindow.Contains(chapterIndex) &&
                !_explicitCacheStatusRequests.Contains(chapterIndex))
            {
                continue;
            }

            if (!_chapterCatalog.TryGetPosition(chapterIndex, out var position))
            {
                continue;
            }

            var status = statusesByChapter.GetValueOrDefault(chapterIndex);
            var formatted = ChapterCachePercentageFormatter.Format(
                status?.CachedSegmentCount ?? 0,
                status?.TotalSegmentCount);
            changed |= !string.Equals(
                _cacheDecorations.TryGet(chapterIndex, out var previous) ? previous : string.Empty,
                formatted,
                StringComparison.Ordinal);
            if (string.IsNullOrEmpty(formatted))
            {
                _cacheDecorations.Remove(chapterIndex);
            }
            else
            {
                _cacheDecorations.Set(chapterIndex, formatted);
            }
            ReplaceChapterItem(position, CreateChapterItem(_chapterCatalog[position]).WithCacheStatus(
                status?.CachedSegmentCount ?? 0,
                status?.TotalSegmentCount),
                notify: !isInitialProjection);
            _explicitCacheStatusRequests.Remove(chapterIndex);
        }

        if (isInitialProjection && changed)
        {
            _chapters.NotifyReset();
        }

        if (!ReferenceEquals(previousCurrentItem, _currentChapterItem))
        {
            OnPropertyChanged(nameof(CurrentChapterItem));
        }
    }

    private void ReportCacheStatusRefreshFailure(Exception exception)
    {
        if (!_isCacheStatusUpdatesActive)
        {
            return;
        }

        var projected = _feedbackService.Project(exception);
        _feedbackService.ShowProjectedNotification("刷新章节缓存进度失败", projected);
    }

    private void ReplaceChapterItem(
        int position,
        BookDetailsChapterItemViewModel item,
        bool notify = true)
    {
        if ((uint)position >= (uint)_chapters.Count &&
            !_chapters.IsReplacing &&
            !_chapters.IsProjectionPending)
        {
            return;
        }

        if ((uint)position >= (uint)_chapters.Count)
        {
            _chapters.ReplaceAt(position, item, notify);
            return;
        }

        var existing = _chapters[position];
        if (existing.IsCurrent == item.IsCurrent &&
            string.Equals(existing.CachePercentageText, item.CachePercentageText, StringComparison.Ordinal))
        {
            return;
        }

        _chapters.ReplaceAt(position, item, notify);
        if (_currentChapterItem?.ChapterIndex == item.ChapterIndex)
        {
            _currentChapterItem = item;
        }
    }

    private BookDetailsChapterItemViewModel CreateChapterItem(
        BookChapterSummary chapter,
        IReadOnlyDictionary<int, bool>? currentSnapshot = null,
        IReadOnlyDictionary<int, string>? cacheSnapshot = null)
    {
        var isCurrent = currentSnapshot is not null
            ? currentSnapshot.TryGetValue(chapter.ChapterIndex, out var current) && current
            : _currentChapterDecoration.TryGet(chapter.ChapterIndex, out current) && current;
        var cachePercentage = cacheSnapshot is not null
            ? cacheSnapshot.TryGetValue(chapter.ChapterIndex, out var cache) ? cache : string.Empty
            : _cacheDecorations.TryGet(chapter.ChapterIndex, out cache) ? cache : string.Empty;
        return new BookDetailsChapterItemViewModel(
            chapter.ChapterIndex,
            $"第 {chapter.ChapterIndex + 1} 章",
            chapter.Title,
            isCurrent,
            cachePercentage);
    }

    private void ResetDetailSupplementProjection()
    {
        _loadedCatalog = [];
        _loadedReadingPosition = null;
        _loadedStatistics = null;
        _chapterCatalog = new IndexedCatalog<BookChapterSummary>(
            [],
            static chapter => chapter.ChapterIndex);
        _currentChapterDecoration.Clear();
        _cacheDecorations.Clear();
        _cacheDecorationWindow.Clear();
        _explicitCacheStatusRequests.Clear();
        _currentChapterItem = null;
        TotalChapterCountText = string.Empty;
        CurrentChapterText = "未开始";
        ChapterCatalogSummaryText = string.Empty;
        ProgressRatio = 0;
        ProgressText = "0%";
        CacheSizeText = "0 B";
        Chapters.Clear();
        _initialCacheStatusProjectionPending = false;
        OnPropertyChanged(nameof(CurrentChapterItem));
    }

    private bool IsCurrentPlaybackBook(string bookId)
    {
        return string.Equals(_playbackCoordinator.CurrentSnapshot.BookId, bookId, StringComparison.Ordinal);
    }

    private void RegisterPlaybackEvents()
    {
        if (_isPlaybackEventsRegistered)
        {
            return;
        }

        _playbackCoordinator.SnapshotChanged += OnPlaybackSnapshotChanged;
        Interlocked.Increment(ref _playbackProjectionVersion);
        _isPlaybackEventsRegistered = true;
    }

    private void OnPlaybackSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        if (!_isPlaybackEventsRegistered)
        {
            return;
        }

        var projectionVersion = Volatile.Read(ref _playbackProjectionVersion);
        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(() => ApplyPlaybackSnapshot(snapshot, projectionVersion)),
                exception => _feedbackService.ShowProjectedNotification(
                    "更新书籍详情播放状态失败",
                    _feedbackService.Project(exception)));
            return;
        }

        ApplyPlaybackSnapshot(snapshot, projectionVersion);
    }

    private void ApplyPlaybackSnapshot(PlaybackSnapshot snapshot, int? expectedProjectionVersion = null)
    {
        if (!_isPlaybackEventsRegistered ||
            _loadedHeader is null ||
            (expectedProjectionVersion is int projectionVersion &&
             (projectionVersion != Volatile.Read(ref _playbackProjectionVersion) ||
              !Equals(_playbackCoordinator.CurrentSnapshot, snapshot))))
        {
            return;
        }

        var previousChapterIndex = _currentChapterItem?.ChapterIndex;
        var progress = EffectiveReadingProgressProjector.Project(
            _loadedHeader.Id,
            _loadedCatalog,
            _loadedReadingPosition,
            snapshot,
            GetChapterPosition);
        ApplyReadingProgress(progress);
        if (previousChapterIndex is int previous &&
            progress.CurrentChapterIndex is int current &&
            previous != current)
        {
            ScheduleCacheStatusRefresh(chapterIndex: null);
        }
    }

    private void ApplyReadingProgress(
        EffectiveReadingProgress progress,
        bool notify = true,
        bool updateItemState = true)
    {
        CurrentChapterText = progress.HasReadingProgress
            ? progress.CurrentChapterTitle
            : "未开始";
        ChapterCatalogSummaryText = progress.HasReadingProgress && progress.CurrentChapterIndex is not null
            ? $"共 {_loadedCatalog.Count} 章 · 当前第 {progress.CurrentChapterIndex.Value + 1} 章"
            : $"共 {_loadedCatalog.Count} 章 · 未开始";
        ProgressRatio = Math.Clamp(progress.OverallProgress, 0, 1);
        ProgressText = $"{ProgressRatio:P0}";

        var nextChapterIndex = progress.HasReadingProgress
            ? progress.CurrentChapterIndex
            : null;
        if (updateItemState && _currentChapterItem is { } previousItem &&
            (!nextChapterIndex.HasValue || previousItem.ChapterIndex != nextChapterIndex.Value))
        {
            _currentChapterDecoration.Remove(previousItem.ChapterIndex);
            if (_chapterCatalog.TryGetPosition(previousItem.ChapterIndex, out var previousPosition))
            {
                ReplaceChapterItem(previousPosition, previousItem.WithCurrentState(false), notify: notify);
            }

            _currentChapterItem = null;
        }

        if (nextChapterIndex is int chapterIndex &&
            _chapterCatalog.TryGetPosition(chapterIndex, out var position))
        {
            _currentChapterDecoration.Set(chapterIndex, true);
            if (position >= _chapters.Count)
            {
                _currentChapterItem = null;
                OnPropertyChanged(nameof(CurrentChapterItem));
                return;
            }

            var currentItem = _chapters[position];
            if (updateItemState)
            {
                var projectedItem = currentItem.WithCurrentState(
                    _currentChapterDecoration.TryGet(chapterIndex, out var isCurrent) && isCurrent);
                ReplaceChapterItem(position, projectedItem, notify: notify);
                _currentChapterItem = _chapters[position];
            }
            else
            {
                _currentChapterItem = currentItem;
            }
        }

        OnPropertyChanged(nameof(CurrentChapterItem));
    }

    private int? GetChapterPosition(int chapterIndex) =>
        _chapterCatalog.TryGetPosition(chapterIndex, out var position) ? position : null;

    private bool DiscardChangesAndContinue()
    {
        CancelEdit();
        return true;
    }

    private void NotifyCommandStateChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanCancelEdit));
        OnPropertyChanged(nameof(CanClearCache));
        SaveCommand.NotifyCanExecuteChanged();
        CancelEditCommand.NotifyCanExecuteChanged();
        ClearCacheCommand.NotifyCanExecuteChanged();
    }

    private static string NormalizeTitle(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string? NormalizeAuthor(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string FormatBytes(long bytes)
    {
        const double scale = 1024d;
        if (bytes < scale)
        {
            return $"{bytes} B";
        }

        var units = new[] { "KB", "MB", "GB", "TB" };
        var size = bytes / scale;
        var unitIndex = 0;
        while (size >= scale && unitIndex < units.Length - 1)
        {
            size /= scale;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }
}
