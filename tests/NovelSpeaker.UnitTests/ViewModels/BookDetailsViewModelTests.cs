using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Dialogs;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Library;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

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
        await WaitForConditionAsync(() => viewModel.Chapters.Count == 3 && !viewModel.IsBusy);

        Assert.Equal("共 3 章", viewModel.TotalChapterCountText);
        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.False(viewModel.IsBusy);
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

        Assert.Equal("缓存已部分清理", feedbackService.LastTitle);
        Assert.Equal("512 B", viewModel.CacheSizeText);
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
        IAppFeedbackService? feedbackService = null,
        FakeAppDialogService? dialogService = null,
        FakeBookDeleteDialogService? deleteDialogService = null,
        FakePlaybackCoordinator? playbackCoordinator = null,
        FakeGuardedNavigationService? guardedNavigationService = null,
        IBookCatalogInvalidationState? invalidationState = null)
    {
        return new BookDetailsViewModel(
            managementService ?? new FakeBookManagementService(),
            cacheWorkspaceService ?? new FakeCacheWorkspaceService(),
            new BookCoverGenerator(),
            feedbackService ?? new FakeFeedbackService(),
            dialogService ?? new FakeAppDialogService(),
            deleteDialogService ?? new FakeBookDeleteDialogService(),
            invalidationState ?? new BookCatalogInvalidationState(),
            playbackCoordinator ?? new FakePlaybackCoordinator(),
            guardedNavigationService ?? new FakeGuardedNavigationService());
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
            "sample.txt",
            "/tmp/sample.txt",
            "utf-8",
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

    private static async Task WaitForConditionAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(predicate());
    }

    private static async Task LoadViewModelAsync(BookDetailsViewModel viewModel)
    {
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        await WaitForConditionAsync(() => !viewModel.IsBusy && viewModel.Chapters.Count == 3);
    }

    private sealed class FakeBookManagementService : IBookManagementService
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

        public Task<BookDetails> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
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
            return Task.FromResult(_details);
        }

        public Task<long> ClearBookCacheAsync(string bookId, CancellationToken cancellationToken)
        {
            if (NextDetailsAfterClear is not null)
            {
                _details = NextDetailsAfterClear;
            }

            return Task.FromResult(0L);
        }

        public Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
        {
            DeleteCallCount++;
            return Task.FromResult<BookDeleteResult?>(new BookDeleteResult(request.BookId, request.DeleteAudioCache, 3, true));
        }
    }

    private sealed class FakeCacheWorkspaceService : ICacheWorkspaceService
    {
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

        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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

        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken)
        {
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

    private sealed class FakePlaybackCoordinator : IPlaybackCoordinator
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
        public Task SkipCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken)
        {
            LastRefreshedBookId = bookId;
            return Task.CompletedTask;
        }

        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken)
        {
            LastDeletedBookId = bookId;
            CurrentSnapshot = PlaybackSnapshot.Idle;
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeGuardedNavigationService : IGuardedNavigationService
    {
        public Type? LastNavigateWithHierarchyPageType { get; private set; }

        public object? LastNavigateWithHierarchyParameter { get; private set; }

        public int GoBackCallCount { get; private set; }

        public bool IsBypassingGuard => false;

        public Task<bool> GoBackAsync(CancellationToken cancellationToken, bool bypassGuard = false)
        {
            GoBackCallCount++;
            return Task.FromResult(true);
        }

        public Task<bool> NavigateAsync(string pageIdOrTargetTag, CancellationToken cancellationToken, bool bypassGuard = false)
        {
            return Task.FromResult(true);
        }

        public Task<bool> NavigateWithHierarchyAsync(
            Type pageType,
            object? dataContext,
            CancellationToken cancellationToken,
            bool bypassGuard = false)
        {
            LastNavigateWithHierarchyPageType = pageType;
            LastNavigateWithHierarchyParameter = dataContext;
            return Task.FromResult(true);
        }
    }
}
