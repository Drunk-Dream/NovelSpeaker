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
    private readonly IBookDetailsQuery _bookDetailsQuery;
    private readonly IBookMetadataUpdateService _bookMetadataUpdateService;
    private readonly IBookDeletionService _bookDeletionService;
    private readonly IAudioCacheStore _cacheStore;
    private readonly ICacheInvalidationCoordinator _invalidationCoordinator;
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
    private readonly BookDetailsProjectionController _projection = new();
    private readonly OwnedTaskRegistry _pageTasks = new();
    private CancellationTokenSource? _activeLoadCancellationTokenSource;
    private bool _stagedLoadStarted;
    private int _loadVersion;
    private int _cacheMutationVersion;
    private int _mutationInProgress;
    private int _headerLoadVersion = -1;
    private BookDetailsHeader? _loadedHeader;
    private BookDetailsStatistics? _loadedStatistics;
    private string? _bookId;
    private CancellationTokenSource? _cacheStatusCancellationTokenSource;
    private bool _isCacheStatusUpdatesActive;
    private bool _isPlaybackEventsRegistered;
    private int _playbackProjectionVersion;

    public BookDetailsViewModel(
        IBookDetailsQuery bookDetailsQuery,
        IBookMetadataUpdateService bookMetadataUpdateService,
        IBookDeletionService bookDeletionService,
        IAudioCacheStore cacheStore,
        ICacheCoverageQuery cacheCoverageQuery,
        ICacheInvalidationCoordinator invalidationCoordinator,
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
        _cacheStore = cacheStore;
        _invalidationCoordinator = invalidationCoordinator;
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
            cacheCoverageQuery,
            _uiScheduler,
            (_, requestedChapterIndices, statuses) =>
                ApplyChapterCacheStatuses(requestedChapterIndices, statuses),
            ReportCacheStatusRefreshFailure);
        Cover = _bookCoverGenerator.Generate("未命名书籍");
    }

    public ObservableCollection<BookDetailsChapterProjection> Chapters => _projection.Chapters;

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

    public BookDetailsChapterProjection? CurrentChapterItem => _projection.CurrentChapterItem;

    public int? CurrentChapterPosition => _projection.CurrentChapterPosition;

    public bool IsChapterCatalogReady => _projection.IsCatalogReady;

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
                _projection.CatalogCount == 0)
            {
                return;
            }

            var chapterIndices = _projection.GetChapterIndices(start, count);
            if (chapterIndices.Count == 0)
            {
                return;
            }

            _projection.SetCacheDecorationWindow(chapterIndices);
            _projection.ClearStaleCacheDecorations(chapterIndices);
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
        Volatile.Write(ref _headerLoadVersion, -1);
        _bookId = bookId;
        ActivateCacheStatusUpdates(cancellationToken);
        var loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeLoadCancellationTokenSource = loadCancellation;
        _stagedLoadStarted = false;
        IsBusy = true;
        StatusMessage = string.Empty;
        ResetDetailSupplementProjection();

        try
        {
            var headerTask = _bookDetailsQuery.GetHeaderAsync(bookId, loadCancellation.Token);
            var catalogTask = _bookDetailsQuery.GetCatalogAsync(bookId, loadCancellation.Token);
            var readingPositionTask = _bookDetailsQuery.GetReadingPositionAsync(bookId, loadCancellation.Token);
            await Task.WhenAll(headerTask, catalogTask, readingPositionTask).ConfigureAwait(true);
            loadCancellation.Token.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _loadVersion))
            {
                return;
            }

            var header = await headerTask.ConfigureAwait(true);
            if (header is null)
            {
                ClearBook();
                StatusMessage = "未找到这本书，可能已经被删除。";
                IsBusy = false;
                NotifyCommandStateChanged();
                return;
            }

            ApplyHeader(header);
            Volatile.Write(ref _headerLoadVersion, version);
            await ApplyCriticalCatalogAsync(
                bookId,
                await catalogTask.ConfigureAwait(true),
                await readingPositionTask.ConfigureAwait(true),
                version,
                loadCancellation).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            loadCancellation.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _loadVersion))
            {
                CancelPendingLoad();
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

    /// <summary>
    /// Starts the non-critical statistics enrichment after the page has published
    /// its first interactive frame. Catalog projection and effective reading position
    /// are part of the critical load and are already available here.
    /// </summary>
    internal void StartStagedLoading()
    {
        var loadVersion = Volatile.Read(ref _loadVersion);
        if (_stagedLoadStarted ||
            _activeLoadCancellationTokenSource is not { IsCancellationRequested: false } loadCancellation ||
            _loadedHeader is null ||
            Volatile.Read(ref _headerLoadVersion) != loadVersion ||
            !_projection.IsCatalogReady ||
            string.IsNullOrWhiteSpace(_bookId))
        {
            return;
        }

        _stagedLoadStarted = true;
        var cacheMutationVersion = Volatile.Read(ref _cacheMutationVersion);
        _pageTasks.Register(
            LoadSecondaryEnrichmentAsync(
                _bookId,
                loadVersion,
                cacheMutationVersion,
                loadCancellation));
    }

    public void HandleNavigatedFrom()
    {
        Interlocked.Increment(ref _loadVersion);
        CancelPendingLoad();
        DeactivateCacheStatusUpdates();
        if (_isPlaybackEventsRegistered)
        {
            _playbackCoordinator.SnapshotChanged -= OnPlaybackSnapshotChanged;
            Interlocked.Increment(ref _playbackProjectionVersion);
            _isPlaybackEventsRegistered = false;
        }

        _stagedLoadStarted = false;
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

        Interlocked.Increment(ref _cacheMutationVersion);
        BeginMutation();
        try
        {
            var result = await _cacheStore.ClearBookAsync(_bookId, cancellationToken);
            var statistics = await _bookDetailsQuery.GetStatisticsAsync(_bookId, cancellationToken);
            if (statistics is not null)
            {
                _loadedStatistics = statistics;
                CacheSizeText = CacheCleanupFeedbackFormatter.FormatBytes(statistics.CachedAudioBytes);
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
            EndMutation();
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

        BeginMutation();
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
            EndMutation();
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
    private async Task SelectChapterAsync(BookDetailsChapterProjection? chapter, CancellationToken cancellationToken)
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

        BeginMutation();
        StatusMessage = string.Empty;

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
            EndMutation();
        }
    }

    private async Task LoadSecondaryEnrichmentAsync(
        string bookId,
        int loadVersion,
        int cacheMutationVersion,
        CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            var statistics = await _bookDetailsQuery.GetStatisticsAsync(
                bookId,
                cancellationTokenSource.Token).ConfigureAwait(true);
            if (!IsCurrentLoad(loadVersion, cancellationTokenSource) ||
                cacheMutationVersion != Volatile.Read(ref _cacheMutationVersion))
            {
                return;
            }

            if (statistics is null)
            {
                ClearBook();
                StatusMessage = "未找到这本书，可能已经被删除。";
                return;
            }

            ApplyStatistics(
                statistics,
                loadVersion,
                cacheMutationVersion,
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
                if (Volatile.Read(ref _mutationInProgress) == 0)
                {
                    IsBusy = false;
                    NotifyCommandStateChanged();
                }
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

    private async Task ApplyCriticalCatalogAsync(
        string bookId,
        IReadOnlyList<BookChapterSummary> catalog,
        BookReadingPosition? readingPosition,
        int loadVersion,
        CancellationTokenSource loadCancellation)
    {
        TotalChapterCountText = $"共 {catalog.Count} 章";
        await _projection.ReplaceCatalogAsync(
            bookId,
            catalog,
            readingPosition,
            _playbackCoordinator.CurrentSnapshot,
            _uiScheduler,
            loadCancellation.Token).ConfigureAwait(true);
        if (!IsCurrentLoad(loadVersion, loadCancellation))
        {
            return;
        }

        var latestSnapshot = _playbackCoordinator.CurrentSnapshot;
        var latestProgress = _projection.ApplyPlaybackSnapshot(bookId, latestSnapshot);
        ApplyReadingProgressState(latestProgress);
        OnPropertyChanged(nameof(CurrentChapterItem));
        OnPropertyChanged(nameof(CurrentChapterPosition));
        OnPropertyChanged(nameof(IsChapterCatalogReady));

        StatusMessage = string.Empty;
        IsBusy = false;
        NotifyCommandStateChanged();
    }

    private void ApplyStatistics(
        BookDetailsStatistics statistics,
        int loadVersion,
        int cacheMutationVersion,
        CancellationTokenSource loadCancellation)
    {
        if (!IsCurrentLoad(loadVersion, loadCancellation) ||
            cacheMutationVersion != Volatile.Read(ref _cacheMutationVersion))
        {
            return;
        }

        _loadedStatistics = statistics;
        CacheSizeText = CacheCleanupFeedbackFormatter.FormatBytes(statistics.CachedAudioBytes);
        NotifyCommandStateChanged();
    }

    private void ClearBook()
    {
        CancelPendingLoad();
        _loadedHeader = null;
        Volatile.Write(ref _headerLoadVersion, -1);
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
        _projection.Reset();
        OnPropertyChanged(nameof(CurrentChapterItem));
        OnPropertyChanged(nameof(CurrentChapterPosition));
        OnPropertyChanged(nameof(IsChapterCatalogReady));
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
        _invalidationCoordinator.BatchPublished += OnInvalidationBatchPublished;
        _settingsService.Changed += OnSettingsChanged;
        _isCacheStatusUpdatesActive = true;
    }

    private void DeactivateCacheStatusUpdates()
    {
        if (_isCacheStatusUpdatesActive)
        {
            _invalidationCoordinator.BatchPublished -= OnInvalidationBatchPublished;
            _settingsService.Changed -= OnSettingsChanged;
            _isCacheStatusUpdatesActive = false;
        }

        _cacheStatusCancellationTokenSource?.Cancel();
        _cacheStatusCancellationTokenSource?.Dispose();
        _cacheStatusCancellationTokenSource = null;
        _cacheStatusRefresh.Deactivate();
    }

    private void OnInvalidationBatchPublished(object? sender, CacheInvalidationBatch batch)
    {
        if (string.IsNullOrWhiteSpace(_bookId))
        {
            return;
        }

        foreach (var change in batch.Changes)
        {
            if (!change.Aspects.HasFlag(CacheInvalidationAspect.PhysicalSummary) &&
                !change.Aspects.HasFlag(CacheInvalidationAspect.Coverage))
            {
                continue;
            }

            switch (change.Scope)
            {
                case CacheInvalidationScope.Global:
                    ScheduleCacheStatusRefresh(chapterIndex: null);
                    break;
                case CacheInvalidationScope.Book book
                    when string.Equals(book.BookId, _bookId, StringComparison.Ordinal):
                    ScheduleCacheStatusRefresh(chapterIndex: null);
                    break;
                case CacheInvalidationScope.Chapters chapters
                    when string.Equals(chapters.BookId, _bookId, StringComparison.Ordinal):
                    foreach (var chapterIndex in chapters.ChapterIndices)
                    {
                        ScheduleCacheStatusRefresh(chapterIndex);
                    }

                    break;
            }
        }
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

    private void QueueCacheStatusRefresh(int? chapterIndex)
    {
        if (!_isCacheStatusUpdatesActive ||
            _cacheStatusCancellationTokenSource is not { IsCancellationRequested: false } ||
            string.IsNullOrWhiteSpace(_bookId))
        {
            return;
        }

        var bookId = _bookId;
        var chapterIndices = chapterIndex is null
            ? _projection.GetCacheDecorationWindow(bookId, _playbackCoordinator.CurrentSnapshot)
            : _projection.ContainsChapter(chapterIndex.Value)
                ? new[] { chapterIndex.Value }
                : Array.Empty<int>();

        if (chapterIndex is null)
        {
            _projection.SetCacheDecorationWindow(chapterIndices);
            _projection.ClearStaleCacheDecorations(chapterIndices);
        }
        else if (chapterIndices.Count > 0)
        {
            _projection.MarkExplicitCacheStatusRequest(chapterIndex.Value);
        }

        _cacheStatusRefresh.Request(bookId, chapterIndices.ToArray());
    }

    private void ApplyChapterCacheStatuses(
        IReadOnlyCollection<int> requestedChapterIndices,
        IReadOnlyCollection<ChapterCacheStatus> statuses)
    {
        if (!_isCacheStatusUpdatesActive)
        {
            return;
        }

        if (_projection.ApplyChapterCacheStatuses(requestedChapterIndices, statuses))
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

    private void ResetDetailSupplementProjection()
    {
        _loadedStatistics = null;
        _projection.Reset();
        TotalChapterCountText = string.Empty;
        CurrentChapterText = "未开始";
        ChapterCatalogSummaryText = string.Empty;
        ProgressRatio = 0;
        ProgressText = "0%";
        CacheSizeText = "0 B";
        OnPropertyChanged(nameof(CurrentChapterItem));
        OnPropertyChanged(nameof(CurrentChapterPosition));
        OnPropertyChanged(nameof(IsChapterCatalogReady));
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

        var previousChapterIndex = _projection.CurrentChapterItem?.ChapterIndex;
        var progress = _projection.ApplyPlaybackSnapshot(_loadedHeader.Id, snapshot);
        ApplyReadingProgressState(progress);
        OnPropertyChanged(nameof(CurrentChapterItem));
        OnPropertyChanged(nameof(CurrentChapterPosition));
        if (previousChapterIndex is int previous &&
            progress.CurrentChapterIndex is int current &&
            previous != current)
        {
            ScheduleCacheStatusRefresh(chapterIndex: null);
        }
    }

    private void ApplyReadingProgressState(EffectiveReadingProgress progress)
    {
        CurrentChapterText = progress.HasReadingProgress
            ? progress.CurrentChapterTitle
            : "未开始";
        ChapterCatalogSummaryText = progress.HasReadingProgress && progress.CurrentChapterIndex is not null
            ? $"共 {_projection.CatalogCount} 章 · 当前第 {progress.CurrentChapterIndex.Value + 1} 章"
            : $"共 {_projection.CatalogCount} 章 · 未开始";
        ProgressRatio = Math.Clamp(progress.OverallProgress, 0, 1);
        ProgressText = $"{ProgressRatio:P0}";
    }

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

    private void BeginMutation()
    {
        Interlocked.Exchange(ref _mutationInProgress, 1);
        IsBusy = true;
        NotifyCommandStateChanged();
    }

    private void EndMutation()
    {
        Interlocked.Exchange(ref _mutationInProgress, 0);
        IsBusy = false;
        NotifyCommandStateChanged();
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

}
