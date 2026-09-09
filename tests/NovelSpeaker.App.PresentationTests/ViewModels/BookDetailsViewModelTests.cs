using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using System.Collections.Specialized;
using System.ComponentModel;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Features.Books.Library;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class BookDetailsViewModelTests
{
    private async Task LoadAsync_projects_read_only_fields_and_chapters()
    {
        var viewModel = CreateViewModel();

        await LoadViewModelAsync(viewModel);

        Assert.Equal("示例小说", viewModel.Title);
        Assert.Equal("作者甲", viewModel.DisplayAuthor);
        Assert.Equal("共 3 章", viewModel.TotalChapterCountText);
        Assert.Equal("第二章 继续", viewModel.CurrentChapterText);
        Assert.Equal("共 3 章 · 当前第 2 章", viewModel.ChapterCatalogSummaryText);
        Assert.Contains("67", viewModel.ProgressText);
        Assert.Equal("2 KB", viewModel.CacheSizeText);
        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.True(viewModel.Chapters[1].IsCurrent);
        Assert.Equal("第二章 继续", viewModel.Chapters[1].TitleToolTip);
    }

    private async Task Playback_snapshot_updates_details_and_catalog_current_item()
    {
        var playbackCoordinator = new FakePlaybackCoordinator();
        var viewModel = CreateViewModel(playbackCoordinator: playbackCoordinator);

        await LoadViewModelAsync(viewModel);

        playbackCoordinator.Publish(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-1",
                ChapterIndex = 2,
                ChapterTitle = "第三章 结尾",
                SegmentIndex = 1,
                SegmentCount = 2
            });

        Assert.Equal("第三章 结尾", viewModel.CurrentChapterText);
        Assert.Equal("共 3 章 · 当前第 3 章", viewModel.ChapterCatalogSummaryText);
        Assert.Equal(1d, viewModel.ProgressRatio);
        Assert.False(viewModel.Chapters[1].IsCurrent);
        Assert.True(viewModel.Chapters[2].IsCurrent);
        Assert.Same(viewModel.Chapters[2], viewModel.CurrentChapterItem);

        playbackCoordinator.Publish(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-2",
                ChapterIndex = 0,
                ChapterTitle = "其它书籍第一章"
            });

        Assert.Equal("第二章 继续", viewModel.CurrentChapterText);
        Assert.True(viewModel.Chapters[1].IsCurrent);
        Assert.Equal(2d / 3d, viewModel.ProgressRatio);

        playbackCoordinator.Publish(PlaybackSnapshot.Idle);

        Assert.Equal("第二章 继续", viewModel.CurrentChapterText);
        Assert.True(viewModel.Chapters[1].IsCurrent);
    }

    private async Task Late_details_result_preserves_newer_playback_snapshot()
    {
        var managementService = new FakeBookManagementService
        {
            BlockDetailsLoad = true
        };
        var playbackCoordinator = new FakePlaybackCoordinator();
        var viewModel = CreateViewModel(
            managementService: managementService,
            playbackCoordinator: playbackCoordinator);

        viewModel.HandleNavigatedTo();
        var loadTask = viewModel.LoadAsync("book-1", CancellationToken.None);
        await Task.Yield();

        playbackCoordinator.Publish(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-1",
                ChapterIndex = 2,
                ChapterTitle = "第三章 结尾"
            });

        managementService.ReleaseBlockedDetailsLoad();
        await loadTask;
        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 3);

        Assert.Equal("第三章 结尾", viewModel.CurrentChapterText);
        Assert.False(viewModel.Chapters[1].IsCurrent);
        Assert.True(viewModel.Chapters[2].IsCurrent);
        Assert.Equal(1d, viewModel.ProgressRatio);
    }

    private async Task Snapshot_after_navigation_away_does_not_update_details()
    {
        var playbackCoordinator = new FakePlaybackCoordinator();
        var viewModel = CreateViewModel(playbackCoordinator: playbackCoordinator);

        await LoadViewModelAsync(viewModel);
        viewModel.HandleNavigatedFrom();

        playbackCoordinator.Publish(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-1",
                ChapterIndex = 2,
                ChapterTitle = "第三章 结尾"
            });

        Assert.Equal("第二章 继续", viewModel.CurrentChapterText);
        Assert.True(viewModel.Chapters[1].IsCurrent);
        Assert.False(viewModel.Chapters[2].IsCurrent);
    }

    private async Task Queued_stale_playback_snapshot_is_ignored_after_page_leave()
    {
        var uiScheduler = new QueuedPlaybackUiScheduler();
        var playbackCoordinator = new FakePlaybackCoordinator();
        var viewModel = CreateViewModel(
            playbackCoordinator: playbackCoordinator,
            uiScheduler: uiScheduler);

        await LoadViewModelAsync(viewModel);
        uiScheduler.QueueActions = true;
        playbackCoordinator.Publish(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-1",
                ChapterIndex = 2,
                ChapterTitle = "第三章 结尾"
            });

        viewModel.HandleNavigatedFrom();
        uiScheduler.RunAll();

        Assert.Equal("第二章 继续", viewModel.CurrentChapterText);
        Assert.True(viewModel.Chapters[1].IsCurrent);
        Assert.False(viewModel.Chapters[2].IsCurrent);
    }

    private async Task LoadAsync_returns_after_critical_catalog_and_stages_statistics()
    {
        var managementService = new FakeBookManagementService();
        var viewModel = CreateViewModel(managementService: managementService);

        await viewModel.LoadAsync("book-1", CancellationToken.None);

        Assert.Equal("示例小说", viewModel.Title);
        Assert.Equal("作者甲", viewModel.DisplayAuthor);
        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.True(viewModel.IsChapterCatalogReady);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(1, managementService.GetBookDetailsHeaderCallCount);
        Assert.Equal(1, managementService.GetBookDetailsCallCount);
        Assert.Equal(0, managementService.GetBookDetailsStatisticsCallCount);

        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => viewModel.Chapters.Count == 3 && !viewModel.IsBusy);

        Assert.Equal("共 3 章", viewModel.TotalChapterCountText);
        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.Equal(1, managementService.GetBookDetailsStatisticsCallCount);
        Assert.False(viewModel.IsBusy);
    }

    private async Task Starting_a_new_load_clears_the_previous_catalog_before_details_finish()
    {
        var managementService = new FakeBookManagementService();
        var viewModel = CreateViewModel(managementService: managementService);

        await LoadViewModelAsync(viewModel);
        managementService.BlockDetailsLoad = true;

        var loadTask = viewModel.LoadAsync("book-1", CancellationToken.None);
        await Task.Yield();

        Assert.Empty(viewModel.Chapters);
        Assert.Null(viewModel.CurrentChapterItem);

        managementService.ReleaseBlockedDetailsLoad();
        await loadTask;
        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 3);
    }

    private async Task Fast_leave_and_reenter_cancels_old_staged_load()
    {
        var managementService = new FakeBookManagementService
        {
            BlockStatisticsLoad = true
        };
        var viewModel = CreateViewModel(managementService: managementService);

        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await Task.Yield();

        viewModel.HandleNavigatedFrom();
        managementService.BlockStatisticsLoad = false;
        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();

        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 3);

        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.True(viewModel.Chapters[1].IsCurrent);
    }

    private async Task Staged_statistics_cannot_release_commands_during_cache_clear()
    {
        var managementService = new FakeBookManagementService
        {
            BlockStatisticsLoad = true
        };
        var cacheDependencies = new FakeCacheDetailsDependencies
        {
            BlockClearBook = true
        };
        var dialogService = new FakeAppDialogService
        {
            NextConfirmationDecision = AppConfirmationDecision.Confirm
        };
        var viewModel = CreateViewModel(
            managementService: managementService,
            cacheDependencies: cacheDependencies,
            dialogService: dialogService);

        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await Task.Yield();

        managementService.BlockStatisticsLoad = false;
        var clearTask = viewModel.ClearCacheCommand.ExecuteAsync(null);
        await Task.Yield();
        managementService.ReleaseBlockedStatisticsLoad();
        await Task.Yield();

        Assert.True(viewModel.IsBusy);

        cacheDependencies.ReleaseBlockedClearBook();
        await clearTask;

        Assert.False(viewModel.IsBusy);
    }

    private async Task Cache_clear_ignores_stale_staged_statistics()
    {
        var managementService = new FakeBookManagementService
        {
            BlockStatisticsLoad = true,
            NextDetailsAfterClear = CreateDetails(cachedAudioBytes: 512)
        };
        var dialogService = new FakeAppDialogService
        {
            NextConfirmationDecision = AppConfirmationDecision.Confirm
        };
        var viewModel = CreateViewModel(
            managementService: managementService,
            dialogService: dialogService);

        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await Task.Yield();

        managementService.BlockStatisticsLoad = false;
        await viewModel.ClearCacheCommand.ExecuteAsync(null);
        Assert.Equal("512 B", viewModel.CacheSizeText);

        managementService.ReleaseBlockedStatisticsLoad();
        await Task.Yield();

        Assert.Equal("512 B", viewModel.CacheSizeText);
    }

    private async Task Loading_a_10000_chapter_catalog_uses_batched_collection_projection()
    {
        var managementService = new FakeBookManagementService
        {
            Details = CreateDetails(10_000, 9_999)
        };
        var viewModel = CreateViewModel(managementService: managementService);
        var changes = new List<NotifyCollectionChangedAction>();
        viewModel.Chapters.CollectionChanged += (_, args) => changes.Add(args.Action);

        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 10_000);

        Assert.Equal(10_000, viewModel.Chapters.Count);
        Assert.NotEmpty(changes);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Add, changes);
        Assert.Equal(NotifyCollectionChangedAction.Reset, changes[^1]);
        Assert.Same(viewModel.Chapters[9_999], viewModel.CurrentChapterItem);
    }

    private async Task Chapter_cache_percentages_refresh_for_cache_and_configuration_changes_until_page_leave()
    {
        var cacheDependencies = new FakeCacheDetailsDependencies
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
            cacheDependencies: cacheDependencies,
            settingsService: settingsService);

        await LoadViewModelAsync(viewModel);
        viewModel.RequestCacheDecorationWindow(0, 32);

        Assert.Equal("25%", viewModel.Chapters[0].CachePercentageText);
        Assert.Equal(string.Empty, viewModel.Chapters[1].CachePercentageText);
        Assert.Equal(string.Empty, viewModel.Chapters[2].CachePercentageText);
        Assert.Equal(1, cacheDependencies.StatusCallCount);
        Assert.Equal(1, cacheDependencies.SubscriberCount);

        var unchangedCurrentItem = viewModel.CurrentChapterItem;
        cacheDependencies.Statuses = [new ChapterCacheStatus(1, 0, 4)];
        cacheDependencies.Publish(CacheInvalidation.ForChapters(
            "book-1", [1], CacheInvalidationAspect.Coverage));
        Assert.Same(unchangedCurrentItem, viewModel.CurrentChapterItem);

        cacheDependencies.Statuses = [new ChapterCacheStatus(1, 3, 4)];
        cacheDependencies.Publish(CacheInvalidation.ForChapters(
            "book-1", [1], CacheInvalidationAspect.Coverage));

        Assert.Equal("75%", viewModel.Chapters[1].CachePercentageText);
        Assert.Equal(3, cacheDependencies.StatusCallCount);

        cacheDependencies.Statuses =
        [
            new ChapterCacheStatus(0, 4, 4),
            new ChapterCacheStatus(1, 4, 4),
            new ChapterCacheStatus(2, 0, 4)
        ];
        settingsService.Publish(settingsService.Current with { DefaultSpeakSpeed = 11 });

        Assert.All(viewModel.Chapters.Take(2), chapter => Assert.Equal("100%", chapter.CachePercentageText));
        Assert.Equal(string.Empty, viewModel.Chapters[2].CachePercentageText);
        Assert.Equal(4, cacheDependencies.StatusCallCount);

        viewModel.HandleNavigatedFrom();
        Assert.Equal(0, cacheDependencies.SubscriberCount);

        cacheDependencies.Publish(CacheInvalidation.ForChapters(
            "book-1", [0], CacheInvalidationAspect.Coverage));
        Assert.Equal(4, cacheDependencies.StatusCallCount);
    }

    private async Task Page_leave_discards_cache_status_projection_that_reaches_the_ui_late()
    {
        var cacheDependencies = new FakeCacheDetailsDependencies
        {
            Statuses = [new ChapterCacheStatus(0, 1, 1)]
        };
        var uiScheduler = new QueuedUiScheduler();
        var viewModel = CreateViewModel(
            cacheDependencies: cacheDependencies,
            uiScheduler: uiScheduler);

        await LoadViewModelAsync(viewModel);
        uiScheduler.QueueActions = true;
        viewModel.RequestCacheDecorationWindow(0, 32);
        Assert.Equal(1, uiScheduler.PendingCount);
        Assert.Equal(string.Empty, viewModel.Chapters[0].CachePercentageText);

        viewModel.HandleNavigatedFrom();
        uiScheduler.RunNext();

        Assert.Equal(string.Empty, viewModel.Chapters[0].CachePercentageText);
    }

    private async Task Moving_current_chapter_refreshes_the_new_cache_window_for_a_10000_chapter_catalog()
    {
        var cacheDependencies = new FakeCacheDetailsDependencies
        {
            StatusHandler = indices => indices.Contains(9_999)
                ? [new ChapterCacheStatus(9_999, 1, 1)]
                : [new ChapterCacheStatus(3, 1, 1)]
        };
        var playbackCoordinator = new FakePlaybackCoordinator();
        var managementService = new FakeBookManagementService
        {
            Details = CreateDetails(10_000, currentChapterIndex: 0)
        };
        var viewModel = CreateViewModel(
            managementService: managementService,
            cacheDependencies: cacheDependencies,
            playbackCoordinator: playbackCoordinator);

        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 10_000);
        viewModel.RequestCacheDecorationWindow(0, 32);
        Assert.Equal(string.Empty, viewModel.Chapters[9_999].CachePercentageText);
        Assert.Equal("100%", viewModel.Chapters[3].CachePercentageText);
        Assert.Same(viewModel.Chapters[0], viewModel.CurrentChapterItem);
        var initialStatusCallCount = cacheDependencies.StatusCallCount;
        var changes = new List<NotifyCollectionChangedEventArgs>();
        viewModel.Chapters.CollectionChanged += (_, eventArgs) => changes.Add(eventArgs);

        playbackCoordinator.Publish(
            PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-1",
                ChapterIndex = 9_999,
                ChapterTitle = "第 10000 章 标题"
            });

        Assert.True(cacheDependencies.StatusCallCount > initialStatusCallCount);
        Assert.Equal("100%", viewModel.Chapters[9_999].CachePercentageText);
        Assert.Equal(string.Empty, viewModel.Chapters[3].CachePercentageText);
        Assert.Contains(
            changes,
            eventArgs => eventArgs.Action == NotifyCollectionChangedAction.Replace &&
                         eventArgs.NewStartingIndex == 3);
    }

    private async Task Visible_cache_status_projection_updates_only_bounded_rows()
    {
        const int chapterCount = 180;
        var cacheDependencies = new FakeCacheDetailsDependencies
        {
            Statuses = Enumerable.Range(0, chapterCount)
                .Select(static index => new ChapterCacheStatus(index, 1, 1))
                .ToArray()
        };
        var uiScheduler = new QueuedUiScheduler();
        var viewModel = CreateViewModel(
            managementService: new FakeBookManagementService
            {
                Details = CreateDetails(chapterCount, currentChapterIndex: 90)
            },
            cacheDependencies: cacheDependencies,
            uiScheduler: uiScheduler);

        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == chapterCount);
        uiScheduler.QueueActions = true;
        viewModel.RequestCacheDecorationWindow(82, 32);

        var collectionChangedCount = 0;
        var resetCount = 0;
        viewModel.Chapters.CollectionChanged += (_, eventArgs) =>
        {
            collectionChangedCount++;
            if (eventArgs.Action == NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }
        };

        Assert.Equal(1, uiScheduler.PendingCount);
        uiScheduler.RunNext();

        Assert.Equal(32, collectionChangedCount);
        Assert.Equal(0, resetCount);
        Assert.All(
            viewModel.Chapters,
            (chapter, index) => Assert.Equal(
                index is >= 82 and < 114 ? "100%" : string.Empty,
                chapter.CachePercentageText));
    }

    private async Task SaveCommand_trims_metadata_and_refreshes_playback_metadata()
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

    private async Task ClearCacheCommand_is_disabled_until_a_book_is_loaded()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.ClearCacheCommand.CanExecute(null));

        await LoadViewModelAsync(viewModel);

        Assert.True(viewModel.ClearCacheCommand.CanExecute(null));
    }

    private async Task BackCommand_with_unsaved_changes_can_save_then_navigate_back()
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

        Assert.Equal(1, navigationService.NavigateBackCallCount);
    }

    private async Task BackCommand_with_unsaved_changes_can_discard_or_cancel()
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

        Assert.Equal(1, navigationService.NavigateBackCallCount);
        Assert.Equal("示例小说", viewModel.EditTitle);

        dialogService.NextUnsavedDecision = UnsavedChangesDecision.Cancel;
        viewModel.EditTitle = "再次修改";

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal(1, navigationService.NavigateBackCallCount);
        Assert.Equal("再次修改", viewModel.EditTitle);
    }

    private async Task SaveCommand_failure_preserves_edit_copy()
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

    private async Task ClearCacheAsync_reloads_details_and_warns_when_cache_remains()
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
        var cacheDependencies = new FakeCacheDetailsDependencies
        {
            ClearBookResult = new AudioCacheStoreCleanupResult(1024, 1, 1, 0)
        };
        var viewModel = CreateViewModel(
            managementService: managementService,
            cacheDependencies: cacheDependencies,
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

    private async Task DeleteBookAsync_for_current_book_stops_playback_and_navigates_back()
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
        Assert.Equal(1, navigationService.NavigateBackCallCount);
        Assert.True(invalidationState.IsInvalidated);
    }

    private async Task SelectChapterCommand_navigates_to_player_with_first_segment_after_confirming_unsaved_changes()
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
        var request = Assert.IsType<PlayerNavigationRequest>(guardedNavigationService.LastNavigationRoute);
        Assert.Equal("book-1", request.BookId);
        Assert.Equal(new BookDetailsRoute("book-1"), request.ReturnRoute);
        Assert.Equal(2, request.ChapterIndex);
        Assert.Equal(0, request.SegmentIndex);
    }

    private async Task ConfirmLeaveAsync_save_failure_returns_false_and_preserves_edit_copy()
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

    private async Task ClearCacheCommand_cancelled_unsaved_changes_does_not_clear_cache()
    {
        var dialogService = new FakeAppDialogService
        {
            NextUnsavedDecision = UnsavedChangesDecision.Cancel
        };
        var cacheDependencies = new FakeCacheDetailsDependencies();
        var viewModel = CreateViewModel(
            dialogService: dialogService,
            cacheDependencies: cacheDependencies);

        await LoadViewModelAsync(viewModel);
        viewModel.EditTitle = "未保存标题";

        await viewModel.ClearCacheCommand.ExecuteAsync(null);

        Assert.Equal(0, cacheDependencies.ClearBookCallCount);
    }

    private async Task DeleteBookCommand_cancelled_unsaved_changes_does_not_delete_book()
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

    [Fact]
    public async Task Book_details_loading_and_playback_contracts_are_isolated()
    {
        await LoadAsync_projects_read_only_fields_and_chapters();
        await Playback_snapshot_updates_details_and_catalog_current_item();
        await Late_details_result_preserves_newer_playback_snapshot();
        await Snapshot_after_navigation_away_does_not_update_details();
        await Queued_stale_playback_snapshot_is_ignored_after_page_leave();
    }

    [Fact]
    public async Task Book_details_loading_lifecycle_contracts_are_isolated()
    {
        await LoadAsync_returns_after_critical_catalog_and_stages_statistics();
        await Starting_a_new_load_clears_the_previous_catalog_before_details_finish();
        await Fast_leave_and_reenter_cancels_old_staged_load();
        await Staged_statistics_cannot_release_commands_during_cache_clear();
        await Cache_clear_ignores_stale_staged_statistics();
    }

    [Fact]
    public Task Book_details_large_catalog_projection_is_batched() =>
        Loading_a_10000_chapter_catalog_uses_batched_collection_projection();

    [Fact]
    public Task Book_details_cache_percentage_refresh_is_scoped() =>
        Chapter_cache_percentages_refresh_for_cache_and_configuration_changes_until_page_leave();

    [Fact]
    public Task Book_details_cache_status_is_discarded_after_page_leave() =>
        Page_leave_discards_cache_status_projection_that_reaches_the_ui_late();

    [Fact]
    public Task Book_details_visible_cache_status_projection_is_bounded() =>
        Visible_cache_status_projection_updates_only_bounded_rows();

    [Fact]
    public Task Book_details_current_chapter_refreshes_cache_window() =>
        Moving_current_chapter_refreshes_the_new_cache_window_for_a_10000_chapter_catalog();

    [Fact]
    public Task Book_details_cache_clear_requires_loaded_book() =>
        ClearCacheCommand_is_disabled_until_a_book_is_loaded();

    [Fact]
    public async Task Book_details_editing_contracts_cover_save_leave_and_metadata()
    {
        await SaveCommand_trims_metadata_and_refreshes_playback_metadata();
        await BackCommand_with_unsaved_changes_can_save_then_navigate_back();
        await BackCommand_with_unsaved_changes_can_discard_or_cancel();
        await SaveCommand_failure_preserves_edit_copy();
        await ConfirmLeaveAsync_save_failure_returns_false_and_preserves_edit_copy();
    }

    [Fact]
    public async Task Book_details_mutation_contracts_cover_cache_clear_delete_and_navigation()
    {
        await ClearCacheAsync_reloads_details_and_warns_when_cache_remains();
        await DeleteBookAsync_for_current_book_stops_playback_and_navigates_back();
        await SelectChapterCommand_navigates_to_player_with_first_segment_after_confirming_unsaved_changes();
        await ClearCacheCommand_cancelled_unsaved_changes_does_not_clear_cache();
        await DeleteBookCommand_cancelled_unsaved_changes_does_not_delete_book();
    }

    private static BookDetailsViewModel CreateViewModel(
        FakeBookManagementService? managementService = null,
        FakeCacheDetailsDependencies? cacheDependencies = null,
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
        cacheDependencies ??= new FakeCacheDetailsDependencies();
        return new BookDetailsViewModel(
            managementService,
            managementService,
            managementService,
            cacheDependencies,
            cacheDependencies,
            cacheDependencies,
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

    private static FakeDetailsState CreateDetails(
        string title = "示例小说",
        string? author = "作者甲",
        long cachedAudioBytes = 2048)
    {
        return new FakeDetailsState(
            new BookDetailsHeader("book-1", title, author),
            [
                new BookChapterSummary(0, "第一章 开始", 0, 120),
                new BookChapterSummary(1, "第二章 继续", 120, 180),
                new BookChapterSummary(2, "第三章 结尾", 300, 90)
            ],
            new BookReadingPosition("book-1", 1, 0, 0, 0, DateTimeOffset.UtcNow),
            new BookDetailsStatistics(cachedAudioBytes));
    }

    private static FakeDetailsState CreateDetails(int chapterCount, int currentChapterIndex)
    {
        return new FakeDetailsState(
            new BookDetailsHeader("book-1", "示例小说", "作者甲"),
            Enumerable.Range(0, chapterCount)
                .Select(index => new BookChapterSummary(
                    index,
                    $"第 {index + 1} 章 标题",
                    index * 100,
                    100))
                .ToArray(),
            new BookReadingPosition("book-1", currentChapterIndex, 0, 0, 0, DateTimeOffset.UtcNow),
            new BookDetailsStatistics(2048));
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
        viewModel.HandleNavigatedTo();
        await viewModel.LoadAsync("book-1", CancellationToken.None);
        viewModel.StartStagedLoading();
        await WaitForConditionAsync(viewModel, () => !viewModel.IsBusy && viewModel.Chapters.Count == 3);
    }

    private sealed record FakeDetailsState(
        BookDetailsHeader Header,
        IReadOnlyList<BookChapterSummary> Catalog,
        BookReadingPosition? ReadingPosition,
        BookDetailsStatistics Statistics);

    private sealed class FakeBookManagementService : IBookDetailsQuery, IBookMetadataUpdateService, IBookDeletionService
    {
        private FakeDetailsState _details = CreateDetails();
        private TaskCompletionSource<IReadOnlyList<BookChapterSummary>>? _blockedDetailsLoadSource;
        private TaskCompletionSource<BookDetailsStatistics?>? _blockedStatisticsLoadSource;
        private BookDetailsStatistics? _blockedStatisticsResult;

        public BookMetadataUpdateRequest? LastUpdateRequest { get; private set; }

        public bool ThrowOnUpdate { get; set; }

        public FakeDetailsState? NextDetailsAfterClear { get; set; }

        public FakeDetailsState? Details { get; init; }

        public bool BlockDetailsLoad { get; set; }

        public bool BlockStatisticsLoad { get; set; }

        public int DeleteCallCount { get; private set; }

        public int GetBookDetailsHeaderCallCount { get; private set; }

        public int GetBookDetailsCallCount { get; private set; }

        public int GetBookDetailsStatisticsCallCount { get; private set; }

        public Task<BookDetailsHeader?> GetHeaderAsync(string bookId, CancellationToken cancellationToken)
        {
            GetBookDetailsHeaderCallCount++;
            var details = GetDetails();
            return Task.FromResult<BookDetailsHeader?>(details.Header);
        }

        public Task<IReadOnlyList<BookChapterSummary>> GetCatalogAsync(string bookId, CancellationToken cancellationToken)
        {
            GetBookDetailsCallCount++;
            var details = GetDetails();
            if (BlockDetailsLoad)
            {
                _blockedDetailsLoadSource = new TaskCompletionSource<IReadOnlyList<BookChapterSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => _blockedDetailsLoadSource.TrySetCanceled(cancellationToken));
                return _blockedDetailsLoadSource.Task;
            }

            return Task.FromResult(details.Catalog);
        }

        public Task<BookReadingPosition?> GetReadingPositionAsync(string bookId, CancellationToken cancellationToken)
            => Task.FromResult(GetDetails().ReadingPosition);

        public Task<BookDetailsStatistics?> GetStatisticsAsync(string bookId, CancellationToken cancellationToken)
        {
            GetBookDetailsStatisticsCallCount++;
            var details = GetDetails();
            if (BlockStatisticsLoad)
            {
                _blockedStatisticsResult = details.Statistics;
                _blockedStatisticsLoadSource = new TaskCompletionSource<BookDetailsStatistics?>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => _blockedStatisticsLoadSource.TrySetCanceled(cancellationToken));
                return _blockedStatisticsLoadSource.Task;
            }

            return Task.FromResult<BookDetailsStatistics?>(details.Statistics);
        }

        private FakeDetailsState GetDetails()
        {
            if (NextDetailsAfterClear is not null)
            {
                _details = NextDetailsAfterClear;
                NextDetailsAfterClear = null;
            }

            return Details ?? _details;
        }

        public void ReleaseBlockedDetailsLoad()
        {
            BlockDetailsLoad = false;
            _blockedDetailsLoadSource?.TrySetResult(GetDetails().Catalog);
        }

        public void ReleaseBlockedStatisticsLoad()
        {
            BlockStatisticsLoad = false;
            _blockedStatisticsLoadSource?.TrySetResult(_blockedStatisticsResult);
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
                Header = _details.Header with
                {
                    Title = request.Title,
                    Author = request.Author
                }
            };
            return Task.FromResult(_details.Header);
        }

        public Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
        {
            DeleteCallCount++;
            return Task.FromResult<BookDeleteResult?>(new BookDeleteResult(request.BookId, request.DeleteAudioCache, 3, true));
        }
    }

    private sealed class FakeCacheDetailsDependencies : IAudioCacheStore, ICacheCoverageQuery, ICacheInvalidationCoordinator
    {
        private EventHandler<CacheInvalidationBatch>? _batchPublished;

        public IReadOnlyList<ChapterCacheStatus> Statuses { get; set; } = [];

        public Func<IReadOnlyCollection<int>, IReadOnlyList<ChapterCacheStatus>>? StatusHandler { get; set; }

        public int StatusCallCount { get; private set; }

        public int SubscriberCount => _batchPublished?.GetInvocationList().Length ?? 0;

        public event EventHandler<CacheInvalidationBatch>? BatchPublished
        {
            add => _batchPublished += value;
            remove => _batchPublished -= value;
        }

        public AudioCacheStoreCleanupResult ClearBookResult { get; set; } = new(2048, 1, 0, 0);

        public int ClearBookCallCount { get; private set; }

        public bool BlockClearBook { get; set; }

        private TaskCompletionSource<AudioCacheStoreCleanupResult>? _blockedClearBookSource;

        public Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CachedBookStoreSummary?> GetBookAsync(string bookId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CachedChapterStoreSummary?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(
            IReadOnlyCollection<CurrentCacheChapterQuery> chapters,
            SynthesisProfileFingerprint synthesisProfile,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesAsync(
            IReadOnlyCollection<AudioCacheKey> keys,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            StatusCallCount++;
            return Task.FromResult(StatusHandler?.Invoke(chapterIndices) ?? Statuses);
        }

        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            IReadOnlyCollection<PlaybackChapterMetadata> chapters,
            CancellationToken cancellationToken) =>
            GetAsync(bookId, chapterIndices, cancellationToken);

        public Task<AudioCacheStoreCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken)
        {
            ClearBookCallCount++;
            if (BlockClearBook)
            {
                _blockedClearBookSource = new TaskCompletionSource<AudioCacheStoreCleanupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => _blockedClearBookSource.TrySetCanceled(cancellationToken));
                return _blockedClearBookSource.Task;
            }

            return Task.FromResult(ClearBookResult);
        }

        public void ReleaseBlockedClearBook()
        {
            BlockClearBook = false;
            _blockedClearBookSource?.TrySetResult(ClearBookResult);
        }

        public Task<AudioCacheStoreCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AudioCacheStoreCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task RunMaintenanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RunStartupMaintenanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Publish(CacheInvalidation invalidation) =>
            _batchPublished?.Invoke(this, new CacheInvalidationBatch([invalidation]));

        public Task FlushPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
        private bool _isExecuting;

        public int PendingCount => _pending.Count;

        public bool QueueActions { get; set; }

        public bool CheckAccess() => !QueueActions || _isExecuting;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            if (!QueueActions || _isExecuting)
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue((action, completion));
            return completion.Task;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void RunNext()
        {
            var pending = _pending.Dequeue();
            _isExecuting = true;
            try
            {
                pending.Action();
                pending.Completion.TrySetResult();
            }
            finally
            {
                _isExecuting = false;
            }
        }
    }

    private sealed class QueuedPlaybackUiScheduler : IUiScheduler
    {
        private readonly Queue<Action> _pending = [];

        public bool QueueActions { get; set; }

        public bool CheckAccess() => !QueueActions;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!QueueActions)
            {
                action();
                return Task.CompletedTask;
            }

            _pending.Enqueue(action);
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }

        public void RunNext()
        {
            _pending.Dequeue()();
        }

        public void RunAll()
        {
            while (_pending.Count > 0)
            {
                RunNext();
            }
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

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeGuardedNavigationService : IAppNavigator
    {
        public AppRoute? LastNavigationRoute { get; private set; }

        public int NavigateBackCallCount { get; private set; }

        public AppRoute CurrentRoute => AppRoutes.Library;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false)
        {
            NavigateBackCallCount++;
            return Task.FromResult(true);
        }

        public Task<bool> NavigateAsync(
            AppRoute route,
            CancellationToken cancellationToken,
            bool bypassGuard = false)
        {
            LastNavigationRoute = route;
            return Task.FromResult(true);
        }
    }
}
