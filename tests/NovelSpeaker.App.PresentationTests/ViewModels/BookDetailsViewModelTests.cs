using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using System.ComponentModel;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class BookDetailsViewModelTests
{
    [Fact]
    public async Task LoadAsync_projects_read_only_fields_and_chapters()
    {
        var viewModel = CreateViewModel();

        await LoadViewModelAsync(viewModel);

        Assert.Equal("示例小说", viewModel.Title);
        Assert.Equal("作者甲", viewModel.DisplayAuthor);
        Assert.Equal("共 3 章", viewModel.TotalChapterCountText);
        Assert.Equal("第二章 继续", viewModel.CurrentChapterText);
        Assert.Equal("共 3 章 · 当前第 2 章", viewModel.ChapterCatalogSummaryText);
        Assert.Contains("40", viewModel.ProgressText);
        Assert.Equal("2 KB", viewModel.CacheSizeText);
        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.True(viewModel.Chapters[1].IsCurrent);
        Assert.Equal("第二章 继续", viewModel.Chapters[1].TitleToolTip);
    }

    [Fact]
    public async Task LoadAsync_returns_after_header_and_populates_catalog_when_background_load_finishes()
    {
        var managementService = new FakeBookManagementService
        {
            BlockDetailsLoad = true
        };
        var viewModel = CreateViewModel(managementService: managementService);

        await viewModel.LoadAsync("book-1", CancellationToken.None);
        await Task.Yield();

        Assert.Equal("示例小说", viewModel.Title);
        Assert.Equal("作者甲", viewModel.DisplayAuthor);
        Assert.Empty(viewModel.Chapters);
        Assert.True(viewModel.IsBusy);
        Assert.Equal(1, managementService.GetBookDetailsHeaderCallCount);
        Assert.Equal(1, managementService.GetBookDetailsCallCount);

        managementService.ReleaseBlockedDetailsLoad();
        await WaitForConditionAsync(viewModel, () => viewModel.Chapters.Count == 3 && !viewModel.IsBusy);

        Assert.Equal("共 3 章", viewModel.TotalChapterCountText);
        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Chapter_cache_percentages_refresh_for_cache_and_configuration_changes_until_page_leave()
    {
        var cacheWorkspace = new FakeCacheWorkspaceService
        {
            Statuses =
            [
                new ChapterCacheStatus(0, 1, 4),
                new ChapterCacheStatus(1, 0, 4),
                new ChapterCacheStatus(2, 1, null)
            ]
        };
        var settingsService = new FakeAppSettingsService();
        var viewModel = CreateViewModel(
            cacheWorkspaceService: cacheWorkspace,
            settingsService: settingsService);

        await LoadViewModelAsync(viewModel);

        Assert.Equal("25%", viewModel.Chapters[0].CachePercentageText);
        Assert.Equal(string.Empty, viewModel.Chapters[1].CachePercentageText);
        Assert.Equal(string.Empty, viewModel.Chapters[2].CachePercentageText);
        Assert.Equal(1, cacheWorkspace.StatusCallCount);
        Assert.Equal(1, cacheWorkspace.SubscriberCount);

        cacheWorkspace.Statuses = [new ChapterCacheStatus(1, 3, 4)];
        cacheWorkspace.Publish(new CacheChangedEventArgs("book-1", 1));

        Assert.Equal("75%", viewModel.Chapters[1].CachePercentageText);
        Assert.Equal(2, cacheWorkspace.StatusCallCount);

        cacheWorkspace.Statuses =
        [
            new ChapterCacheStatus(0, 4, 4),
            new ChapterCacheStatus(1, 4, 4),
            new ChapterCacheStatus(2, 0, 4)
        ];
        settingsService.Publish(settingsService.Current with { DefaultSpeakSpeed = 11 });

        Assert.All(viewModel.Chapters.Take(2), chapter => Assert.Equal("100%", chapter.CachePercentageText));
        Assert.Equal(string.Empty, viewModel.Chapters[2].CachePercentageText);
        Assert.Equal(3, cacheWorkspace.StatusCallCount);

        viewModel.HandleNavigatedFrom();
        Assert.Equal(0, cacheWorkspace.SubscriberCount);

        cacheWorkspace.Publish(new CacheChangedEventArgs("book-1", 0));
        Assert.Equal(3, cacheWorkspace.StatusCallCount);
    }

    [Fact]
    public async Task Page_leave_discards_cache_status_projection_that_reaches_the_ui_late()
    {
        var cacheWorkspace = new FakeCacheWorkspaceService
        {
            Statuses = [new ChapterCacheStatus(0, 1, 1)]
        };
        var uiScheduler = new QueuedUiScheduler();
        var viewModel = CreateViewModel(
            cacheWorkspaceService: cacheWorkspace,
            uiScheduler: uiScheduler);

        await LoadViewModelAsync(viewModel);
        Assert.Equal(1, uiScheduler.PendingCount);
        Assert.Equal(string.Empty, viewModel.Chapters[0].CachePercentageText);

        viewModel.HandleNavigatedFrom();
        uiScheduler.RunNext();

        Assert.Equal(string.Empty, viewModel.Chapters[0].CachePercentageText);
    }

    [Fact]
    public async Task SaveCommand_trims_metadata_and_refreshes_playback_metadata()
    {
        var managementService = new FakeBookManagementService();
        var playbackCoordinator = new FakePlaybackCoordinator();
        var invalidationState = new BookCatalogInvalidationState();
        var viewModel = CreateViewModel(
            managementService: managementService,
            playbackCoordinator: playbackCoordinator,
            invalidationState: invalidationState);

        await LoadViewModelAsync(viewModel);
        viewModel.EditTitle = "  新书名  ";
        viewModel.EditAuthor = "  新作者  ";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("新书名", managementService.LastUpdateRequest!.Title);
        Assert.Equal("新作者", managementService.LastUpdateRequest.Author);
        Assert.Equal("新书名", viewModel.Title);
        Assert.Equal("新作者", viewModel.DisplayAuthor);
        Assert.Equal("book-1", playbackCoordinator.LastRefreshedBookId);
        Assert.True(invalidationState.IsInvalidated);
    }

    [Fact]
    public async Task ClearCacheCommand_is_disabled_until_a_book_is_loaded()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.ClearCacheCommand.CanExecute(null));

        await LoadViewModelAsync(viewModel);

        Assert.True(viewModel.ClearCacheCommand.CanExecute(null));
    }

    [Fact]
    public async Task BackCommand_with_unsaved_changes_can_save_then_navigate_back()
    {
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Save
        };
        var navigationService = new FakeGuardedNavigationService();
        var viewModel = CreateViewModel(
            dialogService: dialogService,
            guardedNavigationService: navigationService);

        await LoadViewModelAsync(viewModel);
        viewModel.EditTitle = "已保存后返回";

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal(1, navigationService.GoBackCallCount);
    }

    [Fact]
    public async Task BackCommand_with_unsaved_changes_can_discard_or_cancel()
    {
        var navigationService = new FakeGuardedNavigationService();
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Discard
        };
        var viewModel = CreateViewModel(
            dialogService: dialogService,
            guardedNavigationService: navigationService);

        await LoadViewModelAsync(viewModel);
        viewModel.EditTitle = "未保存标题";

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal(1, navigationService.GoBackCallCount);
        Assert.Equal("示例小说", viewModel.EditTitle);

        dialogService.NextUnsavedDecision = UnsavedChangesDecision.Cancel;
        viewModel.EditTitle = "再次修改";

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal(1, navigationService.GoBackCallCount);
        Assert.Equal("再次修改", viewModel.EditTitle);
    }

    [Fact]
    public async Task SaveCommand_failure_preserves_edit_copy()
    {
        var managementService = new FakeBookManagementService
        {
            ThrowOnUpdate = true
        };
        var feedbackService = new FakeFeedbackService();
        var viewModel = CreateViewModel(
            managementService: managementService,
            feedbackService: feedbackService);

        await LoadViewModelAsync(viewModel);
        viewModel.EditTitle = "失败后的标题";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("失败后的标题", viewModel.EditTitle);
        Assert.Equal("保存书籍信息失败", feedbackService.LastTitle);
    }

    [Fact]
    public async Task ClearCacheAsync_reloads_details_and_warns_when_cache_remains()
    {
        var dialogService = new FakeAppDialogService
        {
            NextConfirmationDecision = AppConfirmationDecision.Confirm
        };
        var feedbackService = new FakeFeedbackService();
        var managementService = new FakeBookManagementService
        {
            NextDetailsAfterClear = CreateDetails(cachedAudioBytes: 512)
        };
        var cacheWorkspaceService = new FakeCacheWorkspaceService
        {
            ClearBookResult = new CacheCleanupResult(1024, 1, 1, 0)
        };
        var viewModel = CreateViewModel(
            managementService: managementService,
            cacheWorkspaceService: cacheWorkspaceService,
            dialogService: dialogService,
            feedbackService: feedbackService);

        await LoadViewModelAsync(viewModel);
        await viewModel.ClearCacheCommand.ExecuteAsync(null);

        Assert.Equal("清理缓存", dialogService.LastTitle);
        Assert.Equal("缓存已部分清理", feedbackService.LastTitle);
        Assert.Equal("512 B", viewModel.CacheSizeText);
        Assert.Equal("清理", dialogService.LastPrimaryButtonText);
        Assert.StartsWith("将清理这本书的音频缓存", dialogService.LastMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteBookAsync_for_current_book_stops_playback_and_navigates_back()
    {
        var deleteDialogService = new FakeBookDeleteDialogService
        {
            NextResult = new BookDeleteDialogResult(true, false)
        };
        var playbackCoordinator = new FakePlaybackCoordinator(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Paused,
                BookId = "book-1",
                BookTitle = "示例小说"
            });
        var navigationService = new FakeGuardedNavigationService();
        var invalidationState = new BookCatalogInvalidationState();
        var viewModel = CreateViewModel(
            deleteDialogService: deleteDialogService,
            playbackCoordinator: playbackCoordinator,
            guardedNavigationService: navigationService,
            invalidationState: invalidationState);

        await LoadViewModelAsync(viewModel);
        await viewModel.DeleteBookCommand.ExecuteAsync(null);

        Assert.Equal("book-1", playbackCoordinator.LastDeletedBookId);
        Assert.Equal(1, navigationService.GoBackCallCount);
        Assert.True(invalidationState.IsInvalidated);
    }

    [Fact]
    public async Task SelectChapterCommand_navigates_to_player_with_first_segment_after_confirming_unsaved_changes()
    {
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Discard
        };
        var guardedNavigationService = new FakeGuardedNavigationService();
        var viewModel = CreateViewModel(
            dialogService: dialogService,
            guardedNavigationService: guardedNavigationService);

        await LoadViewModelAsync(viewModel);
        viewModel.EditTitle = "待保存的新标题";

        await viewModel.SelectChapterCommand.ExecuteAsync(viewModel.Chapters[2]);

        Assert.Equal("示例小说", viewModel.EditTitle);
        Assert.Equal(typeof(PlayerPage), guardedNavigationService.LastNavigateWithHierarchyPageType);
        var request = Assert.IsType<PlayerNavigationRequest>(guardedNavigationService.LastNavigateWithHierarchyParameter);
        Assert.Equal("book-1", request.BookId);
        Assert.Equal(2, request.ChapterIndex);
        Assert.Equal(0, request.SegmentIndex);
    }

    [Fact]
    public async Task ConfirmLeaveAsync_save_failure_returns_false_and_preserves_edit_copy()
    {
        var managementService = new FakeBookManagementService
        {
            ThrowOnUpdate = true
        };
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Save
        };
        var viewModel = CreateViewModel(
            managementService: managementService,
            dialogService: dialogService);

        await LoadViewModelAsync(viewModel);
        viewModel.EditTitle = "保存失败后仍保留";

        var result = await viewModel.ConfirmLeaveAsync(CancellationToken.None);

        Assert.False(result);
        Assert.Equal("保存失败后仍保留", viewModel.EditTitle);
    }

    [Fact]
    public async Task ClearCacheCommand_cancelled_unsaved_changes_does_not_clear_cache()
    {
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Cancel
        };
        var cacheWorkspaceService = new FakeCacheWorkspaceService();
        var viewModel = CreateViewModel(
            dialogService: dialogService,
            cacheWorkspaceService: cacheWorkspaceService);

        await LoadViewModelAsync(viewModel);
        viewModel.EditTitle = "未保存标题";

        await viewModel.ClearCacheCommand.ExecuteAsync(null);

        Assert.Equal(0, cacheWorkspaceService.ClearBookCallCount);
    }

    [Fact]
    public async Task DeleteBookCommand_cancelled_unsaved_changes_does_not_delete_book()
    {
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Cancel
        };
        var managementService = new FakeBookManagementService();
        var viewModel = CreateViewModel(
            dialogService: dialogService,
            managementService: managementService);

        await LoadViewModelAsync(viewModel);
        viewModel.EditTitle = "未保存标题";

        await viewModel.DeleteBookCommand.ExecuteAsync(null);

        Assert.Equal(0, managementService.DeleteCallCount);
    }

    private static BookDetailsViewModel CreateViewModel(
        FakeBookManagementService? managementService = null,
        FakeCacheWorkspaceService? cacheWorkspaceService = null,
        FakeAppSettingsService? settingsService = null,
        IAppFeedbackService? feedbackService = null,
        FakeAppDialogService? dialogService = null,
        FakeBookDeleteDialogService? deleteDialogService = null,
        FakePlaybackCoordinator? playbackCoordinator = null,
        FakeGuardedNavigationService? guardedNavigationService = null,
        IBookCatalogInvalidationState? invalidationState = null,
        IUiScheduler? uiScheduler = null)
    {
        managementService ??= new FakeBookManagementService();
        return new BookDetailsViewModel(
            managementService,
            managementService,
            managementService,
            cacheWorkspaceService ?? new FakeCacheWorkspaceService(),
            settingsService ?? new FakeAppSettingsService(),
            new BookCoverGenerator(),
            feedbackService ?? new FakeFeedbackService(),
            dialogService ?? new FakeAppDialogService(),
            deleteDialogService ?? new FakeBookDeleteDialogService(),
            invalidationState ?? new BookCatalogInvalidationState(),
            playbackCoordinator ?? new FakePlaybackCoordinator(),
            guardedNavigationService ?? new FakeGuardedNavigationService(),
            uiScheduler ?? new ImmediateUiScheduler());
    }

    private static BookDetails CreateDetails(
        string title = "示例小说",
        string? author = "作者甲",
        long cachedAudioBytes = 2048)
    {
        return new BookDetails(
            "book-1",
            title,
            author,
            3,
            1,
            1,
            0.4,
            true,
            cachedAudioBytes,
            [
                new BookChapterSummary(0, "第一章 开始", 0, 120, false),
                new BookChapterSummary(1, "第二章 继续", 120, 180, true),
                new BookChapterSummary(2, "第三章 结尾", 300, 90, false)
            ]);
    }

    private static async Task WaitForConditionAsync(
        BookDetailsViewModel viewModel,
        Func<bool> predicate)
    {
        if (predicate())
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, _) =>
        {
            if (predicate())
            {
                completion.TrySetResult();
            }
        };
        viewModel.PropertyChanged += handler;
        try
        {
            if (predicate())
            {
                return;
            }

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            viewModel.PropertyChanged -= handler;
        }
    }

    private static async Task LoadViewModelAsync(BookDetailsViewModel viewModel)
    {
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 3);
    }

    private sealed class FakeBookManagementService : IBookLibraryQuery, IBookMetadataUpdateService, IBookDeletionService
    {
        private BookDetails _details = CreateDetails();
        private TaskCompletionSource<BookDetails?>? _blockedDetailsLoadSource;

        public BookMetadataUpdateRequest? LastUpdateRequest { get; private set; }

        public bool ThrowOnUpdate { get; set; }

        public BookDetails? NextDetailsAfterClear { get; set; }

        public bool BlockDetailsLoad { get; set; }

        public int DeleteCallCount { get; private set; }

        public int GetBookDetailsHeaderCallCount { get; private set; }

        public int GetBookDetailsCallCount { get; private set; }

        public Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<BookSummary>>([]);

        public Task<BookDetailsHeader?> GetBookDetailsHeaderAsync(string bookId, CancellationToken cancellationToken)
        {
            GetBookDetailsHeaderCallCount++;
            return Task.FromResult<BookDetailsHeader?>(new BookDetailsHeader(_details.Id, _details.Title, _details.Author));
        }

        public Task<BookDetails?> GetBookDetailsAsync(string bookId, CancellationToken cancellationToken)
        {
            GetBookDetailsCallCount++;
            if (NextDetailsAfterClear is not null)
            {
                _details = NextDetailsAfterClear;
                NextDetailsAfterClear = null;
            }

            if (BlockDetailsLoad)
            {
                _blockedDetailsLoadSource = new TaskCompletionSource<BookDetails?>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => _blockedDetailsLoadSource.TrySetCanceled(cancellationToken));
                return _blockedDetailsLoadSource.Task;
            }

            return Task.FromResult<BookDetails?>(_details);
        }

        public void ReleaseBlockedDetailsLoad()
        {
            BlockDetailsLoad = false;
            _blockedDetailsLoadSource?.TrySetResult(_details);
        }

        public Task<BookDetailsHeader> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
        {
            if (ThrowOnUpdate)
            {
                throw new InvalidOperationException("更新失败");
            }

            LastUpdateRequest = request;
            _details = _details with
            {
                Title = request.Title,
                Author = request.Author
            };
            return Task.FromResult(new BookDetailsHeader(_details.Id, _details.Title, _details.Author));
        }

        public Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
        {
            DeleteCallCount++;
            return Task.FromResult<BookDeleteResult?>(new BookDeleteResult(request.BookId, request.DeleteAudioCache, 3, true));
        }
    }

    private sealed class FakeCacheWorkspaceService : ICacheWorkspaceService
    {
        private EventHandler<CacheChangedEventArgs>? _changed;

        public IReadOnlyList<ChapterCacheStatus> Statuses { get; set; } = [];

        public int StatusCallCount { get; private set; }

        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

        public event EventHandler<CacheChangedEventArgs>? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public CacheCleanupResult ClearBookResult { get; set; } = new(2048, 1, 0, 0);

        public int ClearBookCallCount { get; private set; }

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(string bookId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ChapterCacheStatus>> GetChapterCacheStatusesAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            StatusCallCount++;
            return Task.FromResult(Statuses);
        }

        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken)
        {
            ClearBookCallCount++;
            return Task.FromResult(ClearBookResult);
        }

        public Task<CacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CacheCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public void Publish(CacheChangedEventArgs eventArgs) => _changed?.Invoke(this, eventArgs);
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;

        public event EventHandler<AppSettingsChangedEventArgs>? Changed;

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            Task.FromResult(Current);

        public void Publish(AppSettings settings)
        {
            var previous = Current;
            Current = settings;
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, settings));
        }
    }

    private sealed class ImmediateUiScheduler : IUiScheduler
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }
    }

    private sealed class QueuedUiScheduler : IUiScheduler
    {
        private readonly Queue<(Action Action, TaskCompletionSource Completion)> _pending = [];

        public int PendingCount => _pending.Count;

        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue((action, completion));
            return completion.Task;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void RunNext()
        {
            var pending = _pending.Dequeue();
            pending.Action();
            pending.Completion.TrySetResult();
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public string? LastTitle { get; private set; }

        public string? LastMessage { get; private set; }

        public ProjectedUiError Project(Exception exception)
        {
            return new ExceptionProjector().Project(exception);
        }

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            LastTitle = title;
            LastMessage = projected.UserMessage;
        }

        public void ShowSuccess(string title, string message)
        {
            LastTitle = title;
            LastMessage = message;
        }

        public void ShowWarning(string title, string message)
        {
            LastTitle = title;
            LastMessage = message;
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            LastTitle = title;
            LastMessage = message;
            return Task.FromResult(AppConfirmationDecision.Confirm);
        }
    }

    private sealed class FakeAppDialogService : IAppDialogService
    {
        public AppConfirmationDecision NextConfirmationDecision { get; set; } = AppConfirmationDecision.Cancel;

        public UnsavedChangesDecision NextUnsavedDecision { get; set; } = UnsavedChangesDecision.Cancel;

        public string? LastMessage { get; private set; }

        public string? LastPrimaryButtonText { get; private set; }

        public string? LastTitle { get; private set; }

        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken)
        {
            LastTitle = title;
            LastMessage = message;
            LastPrimaryButtonText = primaryButtonText;
            return Task.FromResult(NextConfirmationDecision);
        }

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(
            string title,
            string message,
            string saveButtonText,
            string discardButtonText,
            string cancelButtonText,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NextUnsavedDecision);
        }
    }

    private sealed class FakeBookDeleteDialogService : IBookDeleteDialogService
    {
        public BookDeleteDialogResult NextResult { get; set; } = new(false, true);

        public Task<BookDeleteDialogResult> ShowAsync(BookDeleteDialogRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakePlaybackCoordinator : IPlaybackBookCommands
    {
        public FakePlaybackCoordinator()
            : this(PlaybackSnapshot.Idle)
        {
        }

        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; private set; }

        public string? LastRefreshedBookId { get; private set; }

        public string? LastDeletedBookId { get; private set; }

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken)
        {
            LastRefreshedBookId = bookId;
            return Task.CompletedTask;
        }

        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken)
        {
            LastDeletedBookId = bookId;
            CurrentSnapshot = PlaybackSnapshot.Idle;
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeGuardedNavigationService : IAppNavigator
    {
        public Type? LastNavigateWithHierarchyPageType { get; private set; }

        public object? LastNavigateWithHierarchyParameter { get; private set; }

        public int GoBackCallCount { get; private set; }

        public Task<bool> GoBackAsync(CancellationToken cancellationToken, bool bypassGuard = false)
        {
            GoBackCallCount++;
            return Task.FromResult(true);
        }

        public Task<bool> NavigateAsync(
            AppRoute route,
            CancellationToken cancellationToken,
            bool bypassGuard = false)
        {
            LastNavigateWithHierarchyPageType = route.Id == AppRouteId.Player ? typeof(PlayerPage) : null;
            LastNavigateWithHierarchyParameter = route;
            return Task.FromResult(true);
        }
    }
}
