using System.IO;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.PresentationTests.TestDoubles;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class CacheManagementViewModelTests
{
    [Fact]
    public async Task LoadAsync_does_not_auto_select_first_book()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult =
            [
                new CachedBookCacheItem("book-1", "第一本", "作者甲", 2, 3, 1024),
                new CachedBookCacheItem("book-2", "第二本", "作者乙", 1, 1, 2048)
            ]
        };
        var viewModel = CreateViewModel(workspaceService);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.HasSelection);
        Assert.True(viewModel.ShowSelectionPrompt);
        Assert.Equal(2, viewModel.Books.Count);
        Assert.DoesNotContain(viewModel.Books, static book => book.IsSelected);
    }

    [Fact]
    public async Task SelectBookAsync_ignores_late_results_from_previous_selection()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult =
            [
                new CachedBookCacheItem("book-1", "第一本", "作者甲", 2, 3, 1024),
                new CachedBookCacheItem("book-2", "第二本", "作者乙", 1, 1, 2048)
            ]
        };
        workspaceService.PendingChapterTasks["book-1"] = new TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        workspaceService.PendingChapterTasks["book-2"] = new TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);

        var firstSelection = viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        var secondSelection = viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[1]);
        workspaceService.PendingChapterTasks["book-2"].SetResult(
        [
            new CachedChapterCacheItem("book-2", 0, "第二本 第一章", 1, 1, 2048, 1)
        ]);
        await secondSelection;

        Assert.Equal("第二本", viewModel.SelectedBookTitle);
        Assert.Single(viewModel.Chapters);
        Assert.Equal("第二本 第一章", viewModel.Chapters[0].Title);

        workspaceService.PendingChapterTasks["book-1"].SetResult(
        [
            new CachedChapterCacheItem("book-1", 0, "第一本 第一章", 1, 1, 1024, 1)
        ]);
        await firstSelection;

        Assert.Equal("第二本", viewModel.SelectedBookTitle);
        Assert.Single(viewModel.Chapters);
        Assert.Equal("第二本 第一章", viewModel.Chapters[0].Title);
    }

    [Fact]
    public async Task Chapter_selection_uses_desktop_modifiers_select_all_and_clear()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult =
            [
                new CachedBookCacheItem("book-1", "第一本", "作者甲", 4, 4, 4096)
            ]
        };
        workspaceService.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "第一章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 1, "第二章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 2, "第三章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 3, "第四章", 1, 1, 1024, 1)
        ];
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        Assert.Equal("完整度：1/1 段 · 100%", viewModel.Chapters[0].CompletenessText);

        viewModel.HandleChapterClick(viewModel.Chapters[1], DesktopSelectionModifiers.None);
        viewModel.HandleChapterClick(viewModel.Chapters[3], DesktopSelectionModifiers.Shift);

        Assert.Equal([1, 2, 3], viewModel.SelectedChapterIndices);
        Assert.True(viewModel.CanClearSelectedChapters);
        Assert.True(viewModel.CanExportSelectedChapters);
        Assert.Equal("已选择 3 章", viewModel.ChapterSelectionSummary);
        Assert.All(viewModel.Chapters.Skip(1), chapter => Assert.True(chapter.IsSelected));

        Assert.True(viewModel.HandleSelectAllChapters());
        Assert.Equal([0, 1, 2, 3], viewModel.SelectedChapterIndices);
        Assert.True(viewModel.HandleClearChapterSelection());
        Assert.Empty(viewModel.SelectedChapterIndices);
        Assert.False(viewModel.CanClearSelectedChapters);
        Assert.False(viewModel.ClearSelectedChaptersCommand.CanExecute(null));
    }

    [Fact]
    public async Task Chapter_card_marks_current_configuration_completeness_as_unavailable()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 1, 4, 4096)]
        };
        workspaceService.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "第一章", 0, 4, 4096, null)
        ];
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.Equal("完整度：配置不可用", Assert.Single(viewModel.Chapters).CompletenessText);
    }

    [Fact]
    public async Task Chapter_cards_project_current_configuration_statuses_without_turning_zero_zero_into_full()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 5, 5, 4096)]
        };
        workspaceService.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "计划缺失", 0, 0, 0, null)
            {
                CurrentConfigurationStatus = ChapterCacheStatusKind.PlanMissing
            },
            new CachedChapterCacheItem("book-1", 1, "计划计算中", 0, 0, 0, null)
            {
                CurrentConfigurationStatus = ChapterCacheStatusKind.PlanUnavailable
            },
            new CachedChapterCacheItem("book-1", 2, "配置不可用", 0, 0, 0, null)
            {
                CurrentConfigurationStatus = ChapterCacheStatusKind.ConfigurationUnavailable
            },
            new CachedChapterCacheItem("book-1", 3, "无可播放内容", 0, 0, 0, 0)
            {
                CurrentConfigurationStatus = ChapterCacheStatusKind.NoPlayableContent
            },
            new CachedChapterCacheItem("book-1", 4, "尚未缓存", 0, 0, 0, 2)
        ];
        var viewModel = CreateViewModel(workspaceService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.Equal("完整度：计划计算中", viewModel.Chapters[0].CompletenessText);
        Assert.Equal("完整度：计划计算中", viewModel.Chapters[1].CompletenessText);
        Assert.Equal("完整度：配置不可用", viewModel.Chapters[2].CompletenessText);
        Assert.Equal("完整度：无可播放内容", viewModel.Chapters[3].CompletenessText);
        Assert.Equal("完整度：0/2 段 · 0%", viewModel.Chapters[4].CompletenessText);
        Assert.DoesNotContain("100%", viewModel.Chapters[3].CompletenessText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Matching_cache_changes_are_coalesced_and_refresh_only_during_page_activation()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 1, 1, 1024)]
        };
        workspaceService.ChaptersResult["book-1"] =
        [new CachedChapterCacheItem("book-1", 0, "第一章", 1, 1, 1024, 1)];
        var viewModel = CreateViewModel(workspaceService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        Assert.Equal(1, workspaceService.ChangedSubscriberCount);

        var firstRefresh = new TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        workspaceService.PendingChapterSequences.Enqueue(firstRefresh);
        var completed = workspaceService.WhenChapterLoadCountReached(3);

        workspaceService.Publish(new CacheChangedEventArgs("book-2", 0));
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 0));
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 1));
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 2));

        await workspaceService.FirstPendingChapterLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var refreshedChapters =
            new[] { new CachedChapterCacheItem("book-1", 0, "刷新后的第一章", 2, 2, 2048, 2) };
        workspaceService.ChaptersResult["book-1"] = refreshedChapters;
        firstRefresh.SetResult(refreshedChapters);
        await completed.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, workspaceService.GetCachedChaptersCallCount);
        Assert.Single(viewModel.Chapters);
        Assert.Equal("刷新后的第一章", viewModel.Chapters[0].Title);

        viewModel.HandleNavigatedFrom();
        Assert.Equal(0, workspaceService.ChangedSubscriberCount);
        var callsAfterLeave = workspaceService.GetCachedChaptersCallCount;
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 0));
        Assert.Equal(callsAfterLeave, workspaceService.GetCachedChaptersCallCount);
    }

    [Fact]
    public async Task Reentering_cache_management_does_not_duplicate_cache_change_subscription()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 1, 1, 1024)]
        };
        workspaceService.ChaptersResult["book-1"] =
        [new CachedChapterCacheItem("book-1", 0, "第一章", 1, 1, 1024, 1)];
        var viewModel = CreateViewModel(workspaceService);

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.HandleNavigatedFrom();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.Equal(1, workspaceService.ChangedSubscriberCount);
        var callsBeforeChange = workspaceService.GetCachedChaptersCallCount;
        var refreshCompleted = workspaceService.WhenChapterLoadCountReached(callsBeforeChange + 1);
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 0));

        await refreshCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(callsBeforeChange + 1, workspaceService.GetCachedChaptersCallCount);
    }

    [Fact]
    public async Task Page_leave_cancels_pending_cache_refresh_and_discards_late_results()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 1, 1, 1024)]
        };
        workspaceService.ChaptersResult["book-1"] =
        [new CachedChapterCacheItem("book-1", 0, "原始章节", 1, 1, 1024, 1)];
        var viewModel = CreateViewModel(workspaceService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        var pendingRefresh = new TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        workspaceService.PendingChapterSequences.Enqueue(pendingRefresh);
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 0));
        await workspaceService.FirstPendingChapterLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.HandleNavigatedFrom();
        await workspaceService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pendingRefresh.SetResult(
        [new CachedChapterCacheItem("book-1", 0, "迟到章节", 2, 2, 2048, 2)]);

        Assert.False(viewModel.IsLoadingChapters);
        Assert.DoesNotContain(viewModel.Chapters, chapter => chapter.Title == "迟到章节");
    }

    [Fact]
    public async Task Switching_books_clears_chapter_selection_without_cross_book_carryover()
    {
        var workspaceService = CreateTwoBookWorkspace();
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);

        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[1]);

        Assert.Empty(viewModel.SelectedChapterIndices);
        Assert.DoesNotContain(viewModel.Chapters, chapter => chapter.IsSelected);
        Assert.Equal("第二本", viewModel.SelectedBookTitle);
    }

    [Fact]
    public async Task Clear_selected_chapters_uses_one_application_batch_request()
    {
        var workspaceService = CreateTwoBookWorkspace();
        workspaceService.ClearChaptersResult = new CacheCleanupResult(2048, 2, 0, 0);
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);
        viewModel.HandleChapterClick(viewModel.Chapters[1], DesktopSelectionModifiers.Control);

        await viewModel.ClearSelectedChaptersCommand.ExecuteAsync(null);

        Assert.Equal(("book-1", new[] { 0, 1 }), workspaceService.LastClearChaptersRequest);
        Assert.Equal(1, workspaceService.ClearChaptersCallCount);
        Assert.Empty(viewModel.SelectedChapterIndices);
    }

    [Fact]
    public async Task Selecting_all_chapters_cleans_the_whole_visible_book_through_batch_boundary()
    {
        var workspaceService = CreateTwoBookWorkspace();
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleSelectAllChapters();

        await viewModel.ClearSelectedChaptersCommand.ExecuteAsync(null);

        Assert.Equal(("book-1", new[] { 0, 1 }), workspaceService.LastClearChaptersRequest);
        Assert.Equal(0, workspaceService.ClearBookCallCount);
    }

    [Fact]
    public async Task Export_command_is_enabled_for_any_selection_and_keeps_unavailable_reasons_accessible()
    {
        var workspace = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 4, 5, 4096)]
        };
        workspace.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "完整", 2, 2, 1024, 2),
            new CachedChapterCacheItem("book-1", 1, "不完整", 1, 1, 1024, 2),
            new CachedChapterCacheItem("book-1", 2, "不可用", 0, 1, 1024, null),
            new CachedChapterCacheItem("book-1", 3, "无可播放段", 0, 1, 1024, 0)
        ];
        var viewModel = CreateViewModel(workspace);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);
        Assert.True(viewModel.CanExportSelectedChapters);
        Assert.True(viewModel.ExportSelectedChaptersCommand.CanExecute(null));

        viewModel.HandleChapterClick(viewModel.Chapters[1], DesktopSelectionModifiers.Control);
        Assert.True(viewModel.CanExportSelectedChapters);
        Assert.True(viewModel.ExportSelectedChaptersCommand.CanExecute(null));
        Assert.Equal("缓存不完整，无法导出", viewModel.Chapters[1].ExportAccessibilityText);
        Assert.Contains("1/2", viewModel.Chapters[1].ExportToolTip, StringComparison.Ordinal);
        Assert.Equal("当前配置不可用，无法导出", viewModel.Chapters[2].ExportAccessibilityText);
        Assert.Equal("没有可播放段落，无法导出", viewModel.Chapters[3].ExportAccessibilityText);
    }

    [Fact]
    public async Task Mixed_selection_cancel_does_not_open_folder_or_start_background_export()
    {
        var coordinator = new FakeChapterExportCoordinator();
        var folders = new FakePresentationFileDialogService { FolderResult = @"D:\Export" };
        var dialogs = new FakeAppDialogService
        {
            NextConfirmationDecision = AppConfirmationDecision.Cancel
        };
        var viewModel = await CreateMixedExportViewModelAsync(coordinator, folders, dialogs);

        await viewModel.ExportSelectedChaptersCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.ConfirmationCallCount);
        Assert.Equal("跳过不可导出章节", dialogs.LastTitle);
        Assert.Equal("跳过并导出", dialogs.LastPrimaryButtonText);
        Assert.Contains("跳过这 1 章并导出其余 1 章", dialogs.LastMessage, StringComparison.Ordinal);
        Assert.Equal(0, folders.PickFolderCallCount);
        Assert.Equal(0, coordinator.StartCallCount);
    }

    [Fact]
    public async Task Navigating_from_during_skip_confirmation_cancels_only_page_owned_preparation()
    {
        var coordinator = new FakeChapterExportCoordinator();
        var folders = new FakePresentationFileDialogService { FolderResult = @"D:\Export" };
        var dialogs = new FakeAppDialogService { WaitForCancellation = true };
        var viewModel = await CreateMixedExportViewModelAsync(coordinator, folders, dialogs);

        var running = viewModel.ExportSelectedChaptersCommand.ExecuteAsync(null);
        await dialogs.ConfirmationStarted.Task;
        viewModel.HandleNavigatedFrom();
        await running;

        Assert.True(dialogs.ObservedCancellation);
        Assert.Equal(0, folders.PickFolderCallCount);
        Assert.Equal(0, coordinator.StartCallCount);
    }

    [Fact]
    public async Task Mixed_selection_confirm_submits_only_exportable_chapters_to_background_coordinator()
    {
        var coordinator = new FakeChapterExportCoordinator();
        var folders = new FakePresentationFileDialogService { FolderResult = @"D:\Export" };
        var dialogs = new FakeAppDialogService();
        var viewModel = await CreateMixedExportViewModelAsync(coordinator, folders, dialogs);

        await viewModel.ExportSelectedChaptersCommand.ExecuteAsync(null);

        var request = Assert.IsType<StartChapterExportRequest>(coordinator.LastRequest);
        Assert.Equal("book-1", request.BookId);
        Assert.Equal("第一本", request.BookTitle);
        Assert.Equal([0], request.Chapters.Select(chapter => chapter.ChapterIndex));
        Assert.Equal(1, request.SkippedChapterCount);
        Assert.Equal(@"D:\Export", request.DestinationRootDirectory);
        Assert.Equal(1, folders.PickFolderCallCount);
    }

    [Fact]
    public async Task All_unavailable_selection_warns_without_confirmation_folder_or_background_export()
    {
        var workspace = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 1, 1, 1024)]
        };
        workspace.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "不完整", 1, 1, 1024, 2)
        ];
        var coordinator = new FakeChapterExportCoordinator();
        var folders = new FakePresentationFileDialogService { FolderResult = @"D:\Export" };
        var dialogs = new FakeAppDialogService();
        var feedback = new FakeFeedbackService();
        var viewModel = CreateViewModel(workspace, feedback, dialogs, coordinator, folders);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);

        Assert.True(viewModel.ExportSelectedChaptersCommand.CanExecute(null));
        await viewModel.ExportSelectedChaptersCommand.ExecuteAsync(null);

        Assert.Equal("没有可导出的章节", feedback.LastTitle);
        Assert.Contains("当前均不可导出", feedback.LastMessage, StringComparison.Ordinal);
        Assert.Equal(0, dialogs.ConfirmationCallCount);
        Assert.Equal(0, folders.PickFolderCallCount);
        Assert.Equal(0, coordinator.StartCallCount);
    }

    [Fact]
    public async Task Export_directory_cancellation_does_not_start_background_export()
    {
        var coordinator = new FakeChapterExportCoordinator();
        var folders = new FakePresentationFileDialogService { FolderResult = null };
        var viewModel = await CreateExportReadyViewModelAsync(coordinator, folders);

        await viewModel.ExportSelectedChaptersCommand.ExecuteAsync(null);

        Assert.Equal(0, coordinator.StartCallCount);
    }

    [Fact]
    public async Task Export_submits_frozen_selection_and_destination_to_background_coordinator()
    {
        var coordinator = new FakeChapterExportCoordinator();
        var folders = new FakePresentationFileDialogService { FolderResult = @"D:\Export" };
        var viewModel = await CreateExportReadyViewModelAsync(coordinator, folders, selectBoth: true);

        await viewModel.ExportSelectedChaptersCommand.ExecuteAsync(null);

        var request = Assert.IsType<StartChapterExportRequest>(coordinator.LastRequest);
        Assert.Equal("book-1", request.BookId);
        Assert.Equal([0, 1], request.Chapters.Select(chapter => chapter.ChapterIndex));
        Assert.Equal(["第一章", "第二章"], request.Chapters.Select(chapter => chapter.ChapterTitle));
        Assert.Equal(@"D:\Export", request.DestinationRootDirectory);
    }

    [Fact]
    public async Task Active_background_export_disables_new_export_without_blocking_cleanup_selection_state()
    {
        var coordinator = new FakeChapterExportCoordinator(CreateExportSnapshot(ChapterExportBatchStatus.Running));
        var viewModel = await CreateExportReadyViewModelAsync(
            coordinator,
            new FakePresentationFileDialogService { FolderResult = @"D:\Export" });

        Assert.False(viewModel.CanExportSelectedChapters);
        Assert.False(viewModel.ExportSelectedChaptersCommand.CanExecute(null));
        Assert.Equal("已有章节导出任务正在运行", viewModel.ExportCommandToolTip);
        Assert.True(viewModel.CanClearSelectedChapters);
    }

    [Fact]
    public async Task Background_export_completion_reenables_export_while_page_is_active()
    {
        var coordinator = new FakeChapterExportCoordinator(CreateExportSnapshot(ChapterExportBatchStatus.Running));
        var viewModel = await CreateExportReadyViewModelAsync(
            coordinator,
            new FakePresentationFileDialogService { FolderResult = @"D:\Export" });

        coordinator.Publish(CreateExportSnapshot(ChapterExportBatchStatus.Completed));

        Assert.True(viewModel.CanExportSelectedChapters);
        Assert.True(viewModel.ExportSelectedChaptersCommand.CanExecute(null));
    }

    [Fact]
    public async Task Navigating_from_page_does_not_cancel_already_started_background_export()
    {
        var coordinator = new FakeChapterExportCoordinator();
        var viewModel = await CreateExportReadyViewModelAsync(
            coordinator,
            new FakePresentationFileDialogService { FolderResult = @"D:\Export" });

        await viewModel.ExportSelectedChaptersCommand.ExecuteAsync(null);
        Assert.Equal(1, coordinator.StartCallCount);

        viewModel.HandleNavigatedFrom();

        Assert.Equal(0, coordinator.CancelCallCount);
    }

    private static ChapterExportSnapshot CreateExportSnapshot(ChapterExportBatchStatus status) =>
        new(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            "book-1",
            "第一本",
            status,
            1,
            status == ChapterExportBatchStatus.Completed ? 1 : 0,
            0,
            status == ChapterExportBatchStatus.Running ? 0 : null,
            status == ChapterExportBatchStatus.Running ? "第一章" : null,
            @"D:\Export",
            status == ChapterExportBatchStatus.Completed ? @"D:\Export\第一本" : null,
            null);

    private static async Task<CacheManagementViewModel> CreateExportReadyViewModelAsync(
        FakeChapterExportCoordinator coordinator,
        FakePresentationFileDialogService folders,
        FakeFeedbackService? feedback = null,
        bool selectBoth = false)
    {
        var workspace = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 2, 2, 2048)]
        };
        workspace.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "第一章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 1, "第二章", 1, 1, 1024, 1)
        ];
        var viewModel = CreateViewModel(workspace, feedback, coordinator: coordinator, fileDialogs: folders);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);
        if (selectBoth)
        {
            viewModel.HandleChapterClick(viewModel.Chapters[1], DesktopSelectionModifiers.Control);
        }

        return viewModel;
    }

    private static async Task<CacheManagementViewModel> CreateMixedExportViewModelAsync(
        FakeChapterExportCoordinator coordinator,
        FakePresentationFileDialogService folders,
        FakeAppDialogService dialogs,
        FakeFeedbackService? feedback = null)
    {
        var workspace = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 2, 2, 2048)]
        };
        workspace.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "完整", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 1, "不完整", 1, 1, 1024, 2)
        ];
        var viewModel = CreateViewModel(workspace, feedback, dialogs, coordinator, folders);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);
        viewModel.HandleChapterClick(viewModel.Chapters[1], DesktopSelectionModifiers.Control);
        return viewModel;
    }

    private static FakeCacheWorkspaceService CreateTwoBookWorkspace()
    {
        var workspace = new FakeCacheWorkspaceService
        {
            BooksResult =
            [
                new CachedBookCacheItem("book-1", "第一本", "作者甲", 2, 2, 2048),
                new CachedBookCacheItem("book-2", "第二本", "作者乙", 1, 1, 1024)
            ]
        };
        workspace.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "第一章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 1, "第二章", 1, 1, 1024, 1)
        ];
        workspace.ChaptersResult["book-2"] =
        [
            new CachedChapterCacheItem("book-2", 0, "另一章", 1, 1, 1024, 1)
        ];
        return workspace;
    }

    private static CacheManagementViewModel CreateViewModel(
        FakeCacheWorkspaceService workspaceService,
        FakeFeedbackService? feedbackService = null,
        FakeAppDialogService? dialogService = null,
        FakeChapterExportCoordinator? coordinator = null,
        FakePresentationFileDialogService? fileDialogs = null)
    {
        return new CacheManagementViewModel(
            workspaceService,
            feedbackService ?? new FakeFeedbackService(),
            dialogService ?? new FakeAppDialogService(),
            new FakeNavigationService(),
            coordinator ?? new FakeChapterExportCoordinator(),
            fileDialogs ?? new FakePresentationFileDialogService());
    }

    private sealed class FakeCacheWorkspaceService : ICacheWorkspaceService
    {
        private readonly Queue<IReadOnlyList<CachedBookCacheItem>> _booksQueue = new();
        private EventHandler<CacheChangedEventArgs>? _changed;
        private TaskCompletionSource? _chapterLoadCompleted;
        private int _chapterLoadCompletionTarget;

        public event EventHandler<CacheChangedEventArgs>? Changed
        {
            add
            {
                _changed += value;
                ChangedSubscriberCount++;
            }
            remove
            {
                _changed -= value;
                ChangedSubscriberCount--;
            }
        }

        public IReadOnlyList<CachedBookCacheItem> BooksResult { get; set; } = [];

        public IReadOnlyList<IReadOnlyList<CachedBookCacheItem>>? BooksSequence
        {
            set
            {
                _booksQueue.Clear();
                if (value is null)
                {
                    return;
                }

                foreach (var item in value)
                {
                    _booksQueue.Enqueue(item);
                }
            }
        }

        public Dictionary<string, CachedChapterCacheItem[]> ChaptersResult { get; } = [];

        public Dictionary<string, TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>> PendingChapterTasks { get; } = [];

        public Queue<TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>> PendingChapterSequences { get; } = [];

        public TaskCompletionSource FirstPendingChapterLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ChangedSubscriberCount { get; private set; }

        public int GetCachedChaptersCallCount { get; private set; }

        public bool LoadChaptersOnBackgroundThread { get; set; }

        public CacheCleanupResult ClearBookResult { get; set; } = new(0, 0, 0, 0);

        public CacheCleanupResult ClearChaptersResult { get; set; } = new(0, 0, 0, 0);

        public (string BookId, int[] ChapterIndices)? LastClearChaptersRequest { get; private set; }

        public int ClearChaptersCallCount { get; private set; }

        public int ClearBookCallCount { get; private set; }

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken)
        {
            if (_booksQueue.Count > 0)
            {
                BooksResult = _booksQueue.Dequeue();
            }

            return Task.FromResult(BooksResult);
        }

        public async Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(string bookId, CancellationToken cancellationToken)
        {
            GetCachedChaptersCallCount++;
            if (PendingChapterSequences.Count > 0)
            {
                var pendingSequence = PendingChapterSequences.Dequeue();
                FirstPendingChapterLoadStarted.TrySetResult();
                IReadOnlyList<CachedChapterCacheItem> chapterItems;
                try
                {
                    chapterItems = await pendingSequence.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved.TrySetResult();
                    throw;
                }
                SignalChapterLoadCompleted();
                return chapterItems;
            }

            if (PendingChapterTasks.TryGetValue(bookId, out var pendingTask))
            {
                var chapterItems = await pendingTask.Task.WaitAsync(cancellationToken);
                SignalChapterLoadCompleted();
                return chapterItems;
            }

            if (LoadChaptersOnBackgroundThread)
            {
                var chapterItems = await Task.Run<IReadOnlyList<CachedChapterCacheItem>>(
                    () => ChaptersResult.TryGetValue(bookId, out var backgroundChapters)
                        ? backgroundChapters
                        : [],
                    cancellationToken);
                SignalChapterLoadCompleted();
                return chapterItems;
            }

            var result = ChaptersResult.TryGetValue(bookId, out var chapters)
                ? chapters
                : [];
            SignalChapterLoadCompleted();
            return result;
        }

        public Task WhenChapterLoadCountReached(int count)
        {
            if (GetCachedChaptersCallCount >= count)
            {
                return Task.CompletedTask;
            }

            _chapterLoadCompleted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _chapterLoadCompletionTarget = count;
            return _chapterLoadCompleted.Task;
        }

        public void Publish(CacheChangedEventArgs eventArgs) => _changed?.Invoke(this, eventArgs);

        private void SignalChapterLoadCompleted()
        {
            if (GetCachedChaptersCallCount >= _chapterLoadCompletionTarget)
            {
                _chapterLoadCompleted?.TrySetResult();
            }
        }

        public Task<IReadOnlyList<ChapterCacheStatus>> GetChapterCacheStatusesAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken)
        {
            ClearBookCallCount++;
            return Task.FromResult(ClearBookResult);
        }

        public Task<CacheCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            ClearChaptersCallCount++;
            LastClearChaptersRequest = (bookId, chapterIndices.ToArray());
            return Task.FromResult(ClearChaptersResult);
        }

        public Task<CacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public string? LastTitle { get; private set; }

        public string? LastMessage { get; private set; }

        public string? LastProjectedMessage { get; private set; }

        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);
        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            LastTitle = title;
            LastProjectedMessage = projected.UserMessage;
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

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken) => Task.FromResult(AppConfirmationDecision.Cancel);
    }

    private sealed class FakePresentationFileDialogService : IPresentationFileDialogService
    {
        public string? FolderResult { get; set; }

        public int PickFolderCallCount { get; private set; }

        public Task<string?> PickOpenFileAsync(
            PresentationFileDialogOptions options,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> PickSaveFileAsync(
            PresentationFileDialogOptions options,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> PickFolderAsync(
            PresentationFolderDialogOptions options,
            CancellationToken cancellationToken)
        {
            PickFolderCallCount++;
            return Task.FromResult(FolderResult);
        }
    }

    private sealed class FakeAppDialogService : IAppDialogService
    {
        public AppConfirmationDecision NextConfirmationDecision { get; set; } = AppConfirmationDecision.Confirm;

        public bool WaitForCancellation { get; set; }

        public bool ObservedCancellation { get; private set; }

        public TaskCompletionSource ConfirmationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConfirmationCallCount { get; private set; }

        public string? LastTitle { get; private set; }

        public string? LastMessage { get; private set; }

        public string? LastPrimaryButtonText { get; private set; }

        public async Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken)
        {
            ConfirmationCallCount++;
            LastTitle = title;
            LastMessage = message;
            LastPrimaryButtonText = primaryButtonText;
            ConfirmationStarted.TrySetResult();
            if (WaitForCancellation)
            {
                var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(
                    () => cancelled.TrySetCanceled(cancellationToken));
                try
                {
                    await cancelled.Task;
                }
                catch (OperationCanceledException)
                {
                    ObservedCancellation = true;
                    throw;
                }
            }

            return NextConfirmationDecision;
        }

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(
            string title,
            string message,
            string saveButtonText,
            string discardButtonText,
            string cancelButtonText,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UnsavedChangesDecision.Cancel);
        }
    }

    private sealed class FakeNavigationService : ITestNavigationService
    {
        public INavigationView GetNavigationControl() => throw new NotSupportedException();
        public bool GoBack() => true;
        public bool Navigate(Type pageType) => true;
        public bool Navigate(Type pageType, object? dataContext) => true;
        public bool Navigate(string pageIdOrTargetTag) => true;
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;
        public bool NavigateWithHierarchy(Type pageType) => true;
        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => true;
        public void SetNavigationControl(INavigationView navigation) { }
    }
}
