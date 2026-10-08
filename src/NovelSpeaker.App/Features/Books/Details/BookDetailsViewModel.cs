using NovelSpeaker.App.Shell.Activation;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;
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
    private readonly ICacheReadModel _readModel;
    private readonly IUiScheduler _uiScheduler;
    private readonly IBookCoverGenerator _bookCoverGenerator;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly IBookDeleteDialogService _deleteDialogService;
    private readonly IBookSourceChangeSource _bookChanges;
    private PageActivationScope? _activation;
    private readonly SemaphoreSlim _bookChangeUpdates = new(1, 1);
    private Task? _criticalLoadTask;
    private readonly IAppNavigator _navigator;
    private readonly IPlaybackSnapshotSource _playbackCoordinator;
    private readonly ChapterCacheViewQuerySlot _cacheStatusRefresh;
    private readonly BookDetailsProjectionController _projection = new();
    private readonly LatestOperationSlot _loadOperation = new();
    private readonly LatestOperationSlot _cacheObservation = new();
    private bool _stagedLoadStarted;
    // Cache clear commits newer physical statistics than an already running enrichment.
    private int _cacheStatisticsRevision;
    private int _mutationInProgress;
    private BookDetailsHeader? _loadedHeader;
    private BookDetailsStatistics? _loadedStatistics;
    private string? _bookId;

    public BookDetailsViewModel(
        IBookDetailsQuery bookDetailsQuery,
        IBookMetadataUpdateService bookMetadataUpdateService,
        IBookDeletionService bookDeletionService,
        IAudioCacheStore cacheStore,
        ICacheReadModel readModel,
        IBookCoverGenerator bookCoverGenerator,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        IBookDeleteDialogService deleteDialogService,
        IBookSourceChangeSource bookChanges,
        IPlaybackSnapshotSource playbackCoordinator,
        IAppNavigator navigator,
        IUiScheduler? uiScheduler = null)
    {
        _bookDetailsQuery = bookDetailsQuery;
        _bookMetadataUpdateService = bookMetadataUpdateService;
        _bookDeletionService = bookDeletionService;
        _cacheStore = cacheStore;
        _readModel = readModel;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        _bookCoverGenerator = bookCoverGenerator;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _deleteDialogService = deleteDialogService;
        _bookChanges = bookChanges;
        _playbackCoordinator = playbackCoordinator;
        _navigator = navigator;
        _cacheStatusRefresh = new ChapterCacheViewQuerySlot(
            readModel,
            _uiScheduler,
            (bookId, requestedChapterIndices, views) =>
            {
                if (string.Equals(_bookId, bookId, StringComparison.Ordinal))
                {
                    ApplyChapterCacheStatuses(requestedChapterIndices, views.Select(static view => view.Coverage).ToArray());
                }
            },
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
    private string displayDescription = string.Empty;

    [ObservableProperty]
    private bool hasDescription;

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

        if (_activation is not { IsCurrent: true } activation ||
            _cacheObservation.Current is not { IsCurrent: true } observation) return;
        var cancellationToken = observation.CancellationToken;
        void Request()
        {
            if (!observation.IsCurrent ||
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
            activation.Register(
                _uiScheduler.InvokeAsync(Request, cancellationToken),
                ReportCacheStatusRefreshFailure);
            return;
        }

        Request();
    }

    public Task LoadAsync(string bookId, CancellationToken cancellationToken)
    {
        var task = _criticalLoadTask = LoadCoreAsync(bookId, cancellationToken);
        _activation?.Register(task);
        return task;
    }

    private async Task LoadCoreAsync(string bookId, CancellationToken cancellationToken, bool preserveEditor = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        if (_activation is not { IsCurrent: true } activation) return;
        var operation = _loadOperation.Begin(cancellationToken, activation);
        _bookId = bookId;
        ActivateCacheStatusUpdates(cancellationToken);

        _stagedLoadStarted = false;
        IsBusy = true;
        StatusMessage = string.Empty;
        ResetDetailSupplementProjection();

        try
        {
            var headerTask = _bookDetailsQuery.GetHeaderAsync(bookId, operation.CancellationToken);
            var catalogTask = _bookDetailsQuery.GetCatalogAsync(bookId, operation.CancellationToken);
            var readingPositionTask = _bookDetailsQuery.GetReadingPositionAsync(bookId, operation.CancellationToken);
            await Task.WhenAll(headerTask, catalogTask, readingPositionTask).ConfigureAwait(true);
            operation.CancellationToken.ThrowIfCancellationRequested();
            if (!operation.IsCurrent)
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

            ApplyHeader(header, preserveEditor);
            await ApplyCriticalCatalogAsync(
                bookId,
                await catalogTask.ConfigureAwait(true),
                await readingPositionTask.ConfigureAwait(true),
                operation).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            operation.CancellationToken.IsCancellationRequested)
        {
            if (ReferenceEquals(_loadOperation.Current, operation))
            {
                CancelPendingLoad();
                IsBusy = false;
                NotifyCommandStateChanged();
            }

            if (cancellationToken.IsCancellationRequested || _activation is not { IsCurrent: true }) throw;
        }
        catch (Exception exception)
        {
            if (!operation.IsCurrent)
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
        if (_stagedLoadStarted ||
            _activation is not { IsCurrent: true } activation ||
            _loadOperation.Current is not { IsCurrent: true } operation ||
            _loadedHeader is null || !_projection.IsCatalogReady ||
            string.IsNullOrWhiteSpace(_bookId)) return;

        _stagedLoadStarted = true;
        var cacheStatisticsRevision = Volatile.Read(ref _cacheStatisticsRevision);
        activation.Register(LoadSecondaryEnrichmentAsync(_bookId, cacheStatisticsRevision, operation));
    }

    public void HandleNavigatedFrom()
    {
        var activation = _activation;
        _activation = null;
        activation?.Dispose();
        CancelPendingLoad();
        DeactivateCacheStatusUpdates();
        _stagedLoadStarted = false;
        IsBusy = false;
        NotifyCommandStateChanged();
    }

    public void HandleNavigatedTo(PageActivationScope activation)
    {
        _activation?.Dispose();
        _activation = activation;
        activation.Register(() =>
        {
            if (ReferenceEquals(_activation, activation)) HandleNavigatedFrom();
        });
        _playbackCoordinator.SnapshotChanged += OnPlaybackSnapshotChanged;
        _bookChanges.Changed += OnBookCommittedChange;
        _readModel.Changed += OnCacheReadModelChanged;
        activation.Register(() =>
        {
            _playbackCoordinator.SnapshotChanged -= OnPlaybackSnapshotChanged;
            _bookChanges.Changed -= OnBookCommittedChange;
            _readModel.Changed -= OnCacheReadModelChanged;
        });
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

        Interlocked.Increment(ref _cacheStatisticsRevision);
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

        var deletedTitle = _loadedHeader.Title;
        BeginMutation();
        try
        {
            var result = await _bookDeletionService.DeleteAsync(
                new BookDeleteRequest(_bookId, deleteDecision.DeleteAudioCache),
                cancellationToken);
            if (result is null)
            {
                StatusMessage = "这本书已不存在。";
                await _navigator.NavigateBackAsync(cancellationToken, bypassGuard: true).ConfigureAwait(true);
                return;
            }

            _feedbackService.ShowSuccess("删除成功", $"已删除《{deletedTitle}》。");
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
        int cacheStatisticsRevision,
        LatestOperationSlot.Operation operation)
    {
        try
        {
            var statistics = await _bookDetailsQuery.GetStatisticsAsync(
                bookId,
                operation.CancellationToken).ConfigureAwait(true);
            if (!operation.IsCurrent ||
                cacheStatisticsRevision != Volatile.Read(ref _cacheStatisticsRevision))
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
                cacheStatisticsRevision,
                operation);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!operation.IsCurrent)
            {
                return;
            }

            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("加载书籍详情失败", projected);
        }
        finally
        {
            var wasCurrent = operation.IsCurrent;
            operation.Dispose();
            if (wasCurrent)
            {
                if (Volatile.Read(ref _mutationInProgress) == 0)
                {
                    IsBusy = false;
                    NotifyCommandStateChanged();
                }
            }
        }
    }

    private void ApplyHeader(BookDetailsHeader header, bool preserveEditor = false)
    {
        var hadUnsavedChanges = HasUnsavedChanges;
        _loadedHeader = header;
        HasBook = true;
        Title = header.Title;
        DisplayAuthor = string.IsNullOrWhiteSpace(header.Author) ? "未知作者" : header.Author.Trim();
        DisplayDescription = header.Description?.Trim() ?? string.Empty;
        HasDescription = DisplayDescription.Length > 0;
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
        LatestOperationSlot.Operation operation)
    {
        TotalChapterCountText = $"共 {catalog.Count} 章";
        await _projection.ReplaceCatalogAsync(
            bookId,
            catalog,
            readingPosition,
            _playbackCoordinator.CurrentSnapshot,
            _uiScheduler,
            operation.CancellationToken).ConfigureAwait(true);
        if (!operation.IsCurrent)
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
        int cacheStatisticsRevision,
        LatestOperationSlot.Operation operation)
    {
        if (!operation.IsCurrent ||
            cacheStatisticsRevision != Volatile.Read(ref _cacheStatisticsRevision))
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
        _loadedStatistics = null;
        IsBusy = false;
        HasBook = false;
        Title = string.Empty;
        EditTitle = string.Empty;
        EditAuthor = string.Empty;
        DisplayAuthor = "未知作者";
        DisplayDescription = string.Empty;
        HasDescription = false;
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

    private void CancelPendingLoad() => _loadOperation.Cancel();

    private void ActivateCacheStatusUpdates(CancellationToken cancellationToken)
    {
        DeactivateCacheStatusUpdates();
        if (_activation is not { IsCurrent: true } activation) return;
        var observation = _cacheObservation.Begin(cancellationToken, activation);
        _cacheStatusRefresh.Activate(observation.CancellationToken, activation);
    }

    private void DeactivateCacheStatusUpdates()
    {
        _cacheObservation.Cancel();
        _cacheStatusRefresh.Deactivate();
    }

    private void OnCacheReadModelChanged(object? sender, CacheReadModelChange change)
    {
        if (_activation is not { IsCurrent: true } activation ||
            _cacheObservation.Current is not { IsCurrent: true } observation) return;
        activation.Register(_uiScheduler.InvokeAsync(() =>
        {
            if (observation.IsCurrent) ApplyCacheReadModelChange(change);
        }, observation.CancellationToken), ReportCacheStatusRefreshFailure);
    }

    private void ApplyCacheReadModelChange(CacheReadModelChange change)
    {
        if (string.IsNullOrWhiteSpace(_bookId))
        {
            return;
        }

        foreach (var scope in change.Scopes)
        {
            switch (scope)
            {
                case CacheReadModelScope.Global:
                    RefreshObservedCacheWindow(_bookId);
                    break;
                case CacheReadModelScope.Book book
                    when string.Equals(book.BookId, _bookId, StringComparison.Ordinal):
                    RefreshObservedCacheWindow(_bookId);
                    break;
                case CacheReadModelScope.Chapters chapters
                    when string.Equals(chapters.BookId, _bookId, StringComparison.Ordinal):
                    foreach (var chapterIndex in chapters.ChapterIndices)
                    {
                        ScheduleCacheStatusRefresh(chapterIndex);
                    }

                    break;
            }
        }
    }

    private void RefreshObservedCacheWindow(string bookId)
    {
        if (_projection.CacheDecorationWindow.Count == 0)
        {
            QueueCacheStatusRefresh(chapterIndex: null);
            return;
        }

        var indices = _projection.CacheDecorationWindow.ToHashSet();
        if (_projection.CurrentChapterItem is { } current)
        {
            indices.Add(current.ChapterIndex);
            _projection.MarkExplicitCacheStatusRequest(current.ChapterIndex);
        }

        _cacheStatusRefresh.Request(bookId, indices);
    }

    private void ScheduleCacheStatusRefresh(int? chapterIndex)
    {
        if (_activation is not { IsCurrent: true } activation ||
            _cacheObservation.Current is not { IsCurrent: true } observation) return;
        var cancellationToken = observation.CancellationToken;
        if (!_uiScheduler.CheckAccess())
        {
            activation.Register(
                _uiScheduler.InvokeAsync(() =>
                {
                    if (observation.IsCurrent) QueueCacheStatusRefresh(chapterIndex);
                }, cancellationToken),
                ReportCacheStatusRefreshFailure);
            return;
        }

        QueueCacheStatusRefresh(chapterIndex);
    }

    private void QueueCacheStatusRefresh(int? chapterIndex)
    {
        if (_cacheObservation.Current is not { IsCurrent: true } ||
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
        if (_cacheObservation.Current is not { IsCurrent: true })
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
        if (_cacheObservation.Current is not { IsCurrent: true })
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

    private void OnBookCommittedChange(object? sender, BookCommittedChange change)
    {
        if (_activation is not { IsCurrent: true } activation || change.BookId != _bookId) return;
        activation.Run(token => _uiScheduler.InvokeLaterAsync(() =>
        {
            if (!activation.IsCurrent || change.BookId != _bookId) return;
            if (change is BookCommittedChange.BookRemoved or BookCommittedChange.ActiveCatalogCommitted or BookCommittedChange.ActiveSourceChanged)
            {
                CancelPendingLoad();
            }
            activation.Run(ct => RefreshBookAsync(change, ct), ReportBookRefreshFailure);
        }, token), ReportBookRefreshFailure);
    }

    private async Task RefreshBookAsync(BookCommittedChange change, CancellationToken cancellationToken)
    {
        await _bookChangeUpdates.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (change.BookId != _bookId) return;
            while (_criticalLoadTask is { } loading)
            {
                try { await loading.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                if (ReferenceEquals(loading, _criticalLoadTask)) break;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (change.BookId != _bookId) return;
            if (change is BookCommittedChange.MetadataCommitted)
            {
                var loadIdentity = _loadOperation.Identity;
                var header = await _bookDetailsQuery.GetHeaderAsync(change.BookId, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (loadIdentity != _loadOperation.Identity) return;
                if (header is not null) ApplyHeader(header, preserveEditor: true);
            }
            else if (change is BookCommittedChange.BookRemoved)
            {
                CancelPendingLoad();
                DeactivateCacheStatusUpdates();
                ClearBook();
                StatusMessage = "这本书已不存在。";
            }
            else if (change is not BookCommittedChange.SourceRemoved)
            {
                await (_criticalLoadTask = LoadCoreAsync(change.BookId, cancellationToken, preserveEditor: true));
                StartStagedLoading();
            }
        }
        finally
        {
            _bookChangeUpdates.Release();
        }
    }

    private void ReportBookRefreshFailure(Exception exception) =>
        _feedbackService.ShowProjectedNotification("刷新书籍详情失败", _feedbackService.Project(exception));

    private void OnPlaybackSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        if (_activation is not { IsCurrent: true } activation) return;
        var bookId = _bookId;
        void Apply()
        {
            if (activation.IsCurrent && bookId == _bookId && Equals(_playbackCoordinator.CurrentSnapshot, snapshot))
                ApplyPlaybackSnapshot(snapshot);
        }
        if (!_uiScheduler.CheckAccess())
        {
            activation.Register(_uiScheduler.InvokeAsync(Apply, activation.CancellationToken),
                exception => _feedbackService.ShowProjectedNotification(
                    "更新书籍详情播放状态失败", _feedbackService.Project(exception)));
            return;
        }
        Apply();
    }

    private void ApplyPlaybackSnapshot(PlaybackSnapshot snapshot)
    {
        if (_activation is not { IsCurrent: true } || _loadedHeader is null ||
            _loadedHeader.Id != _bookId || !_projection.IsCatalogReady) return;

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
        ChapterCatalogSummaryText = _loadedHeader?.ActiveSource is null && _projection.CatalogCount == 0
            ? "无当前来源"
            : progress.HasReadingProgress && progress.CurrentChapterIndex is not null
            ? $"共 {_projection.CatalogCount} 章 · 当前：{progress.CurrentChapterTitle}"
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
