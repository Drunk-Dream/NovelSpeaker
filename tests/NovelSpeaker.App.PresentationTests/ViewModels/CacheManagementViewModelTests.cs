using System.IO;
using System.Collections.Specialized;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.PresentationTests.TestDoubles;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class CacheManagementViewModelTests
{
    private async Task LoadAsync_does_not_auto_select_first_book()
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

    private async Task SelectBookAsync_ignores_late_results_from_previous_selection()
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

    private async Task Chapter_selection_uses_desktop_modifiers_select_all_and_clear()
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
            new CachedChapterCacheItem("book-1", 0, "第一章", 3, 1, 1024, 2),
            new CachedChapterCacheItem("book-1", 1, "第二章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 2, "第三章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 3, "第四章", 1, 1, 1024, 1)
        ];
        workspaceService.CoverageCachedSegmentCounts[("book-1", 0)] = 1;
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        Assert.True(workspaceService.CoverageQueryCallCount > 0);
        Assert.Equal("完整度：1/2 段 · 50%", viewModel.Chapters[0].CompletenessText);

        viewModel.HandleChapterClick(viewModel.Chapters[1], DesktopSelectionModifiers.None);
        viewModel.HandleChapterClick(viewModel.Chapters[3], DesktopSelectionModifiers.Shift);

        Assert.Equal([1, 2, 3], viewModel.SelectedChapterIndices);
        Assert.True(viewModel.CanClearSelectedChapters);
        Assert.True(viewModel.CanExportSelectedChapters);
        Assert.Equal("已选择 3 章", viewModel.ChapterSelectionSummary);
        Assert.All(viewModel.Chapters.Skip(1), chapter => Assert.True(chapter.IsSelected));

        Assert.True(viewModel.HandleSelectAllChapters());
        Assert.Equal([0, 1, 2, 3], viewModel.SelectedChapterIndices);
        Assert.True(viewModel.TryHandleEscape());
        Assert.Empty(viewModel.SelectedChapterIndices);
        Assert.False(viewModel.CanClearSelectedChapters);
        Assert.False(viewModel.ClearSelectedChaptersCommand.CanExecute(null));
    }

    private async Task Coverage_only_invalidation_refreshes_current_configuration_status()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 1, 1, 1024)]
        };
        workspaceService.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "第一章", 0, 0, 0, 2)
        ];
        workspaceService.CoverageCachedSegmentCounts[("book-1", 0)] = 0;
        var viewModel = CreateViewModel(workspaceService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        var coverageQueryCount = workspaceService.CoverageQueryCallCount;

        workspaceService.CoverageCachedSegmentCounts[("book-1", 0)] = 2;
        workspaceService.Publish(CacheInvalidation.ForChapters(
            "book-1",
            [0],
            CacheInvalidationAspect.Coverage));

        Assert.True(workspaceService.CoverageQueryCallCount > coverageQueryCount);
        Assert.Equal("完整度：2/2 段 · 100%", viewModel.Chapters[0].CompletenessText);
    }

    private async Task Loading_a_10000_chapter_cache_catalog_avoids_item_by_item_add_notifications()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 10_000, 10_000, 1024)]
        };
        workspaceService.ChaptersResult["book-1"] = Enumerable.Range(0, 10_000)
            .Select(index => new CachedChapterCacheItem(
                "book-1",
                index,
                $"第 {index + 1} 章",
                1,
                1,
                1024,
                1))
            .ToArray();
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        var changes = new List<NotifyCollectionChangedAction>();
        viewModel.Chapters.CollectionChanged += (_, args) => changes.Add(args.Action);

        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.Equal(10_000, viewModel.Chapters.Count);
        Assert.NotEmpty(changes);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Add, changes);
        Assert.Contains(NotifyCollectionChangedAction.Reset, changes);

        changes.Clear();
        Assert.True(viewModel.HandleSelectAllChapters());
        Assert.Equal([NotifyCollectionChangedAction.Reset], changes);
        Assert.All(viewModel.Chapters, static chapter => Assert.True(chapter.IsSelected));
    }

    private async Task Chapter_card_marks_current_configuration_completeness_as_unavailable()
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

    private async Task Chapter_cards_project_current_configuration_statuses_without_turning_zero_zero_into_full()
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

    private async Task Matching_cache_changes_are_coalesced_and_refresh_only_during_page_activation()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 3, 3, 3072)]
        };
        workspaceService.ChaptersResult["book-1"] =
        [
            new CachedChapterCacheItem("book-1", 0, "第一章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 1, "第二章", 1, 1, 1024, 1),
            new CachedChapterCacheItem("book-1", 2, "第三章", 1, 1, 1024, 1)
        ];
        var viewModel = CreateViewModel(workspaceService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        Assert.Equal(1, workspaceService.InvalidationSubscriberCount);
        var unchangedChapter = viewModel.Chapters[1];
        await workspaceService.WhenChapterLoadCountReached(2);

        var firstRefresh = new TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        workspaceService.PendingChapterSequences.Enqueue(firstRefresh);
        var previousChapterBatchCalls = workspaceService.GetCachedChaptersCallCount;
        var completed = workspaceService.WhenChapterLoadCountReached(previousChapterBatchCalls + 4);

        workspaceService.Publish(new CacheChangedEventArgs("book-2", 0));
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 0));
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 0));
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 0));

        await workspaceService.FirstPendingChapterLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var refreshedChapters =
            new[]
            {
                new CachedChapterCacheItem("book-1", 0, "刷新后的第一章", 2, 2, 2048, 2),
                new CachedChapterCacheItem("book-1", 1, "第二章", 1, 1, 1024, 1),
                new CachedChapterCacheItem("book-1", 2, "第三章", 1, 1, 1024, 1)
            };
        workspaceService.ChaptersResult["book-1"] = refreshedChapters;
        firstRefresh.SetResult(refreshedChapters);
        await completed.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(previousChapterBatchCalls + 4, workspaceService.GetCachedChaptersCallCount);
        Assert.Equal(3, viewModel.Chapters.Count);
        Assert.Equal("刷新后的第一章", viewModel.Chapters[0].Title);
        Assert.Same(unchangedChapter, viewModel.Chapters[1]);

        viewModel.HandleNavigatedFrom();
        Assert.Equal(0, workspaceService.InvalidationSubscriberCount);
        var callsAfterLeave = workspaceService.GetCachedChapterCallCount;
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 0));
        Assert.Equal(callsAfterLeave, workspaceService.GetCachedChapterCallCount);
    }

    private async Task Matching_cache_changes_refresh_only_the_targeted_book_summary()
    {
        var workspaceService = CreateTwoBookWorkspace();
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

        var unchangedBook = viewModel.Books[1];
        var collectionChanges = new List<NotifyCollectionChangedAction>();
        viewModel.Books.CollectionChanged += (_, args) => collectionChanges.Add(args.Action);
        workspaceService.BooksResult =
        [
            new CachedBookCacheItem("book-1", "更新后的第一本", "作者甲", 4, 4, 512),
            new CachedBookCacheItem("book-2", "第二本", "作者乙", 1, 1, 1024)
        ];
        var previousBookSummaryCalls = workspaceService.GetCachedBooksCallCount;
        var refreshCompleted = workspaceService.WhenBookLoadCountReached(previousBookSummaryCalls + 1);
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 0));

        await refreshCompleted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(unchangedBook, viewModel.Books[0]);
        Assert.Equal("更新后的第一本", viewModel.Books[1].Title);
        Assert.True(viewModel.Books[1].IsSelected);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, collectionChanges);
    }

    private async Task Reentering_cache_management_does_not_duplicate_cache_change_subscription()
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

        Assert.Equal(1, workspaceService.InvalidationSubscriberCount);
        var callsBeforeChange = workspaceService.GetCachedChaptersCallCount;
        var refreshCompleted = workspaceService.WhenChapterLoadCountReached(callsBeforeChange + 2);
        workspaceService.Publish(new CacheChangedEventArgs("book-1", 0));

        await refreshCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(callsBeforeChange + 2, workspaceService.GetCachedChaptersCallCount);
    }

    private async Task Invalidation_refreshes_another_book_without_disturbing_current_selection()
    {
        var workspaceService = CreateTwoBookWorkspace();
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[1]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);

        workspaceService.BooksResult =
        [
            new CachedBookCacheItem("book-1", "第一本（更新）", "作者甲", 2, 4, 4096),
            new CachedBookCacheItem("book-2", "第二本", "作者乙", 1, 1, 1024)
        ];
        var previousBookSummaryCalls = workspaceService.GetCachedBooksCallCount;
        workspaceService.Publish(CacheInvalidation.ForBook(
            "book-1",
            CacheInvalidationAspect.PhysicalSummary));

        Assert.Equal(previousBookSummaryCalls + 1, workspaceService.GetCachedBooksCallCount);
        Assert.Contains(viewModel.Books, book => book.Title == "第一本（更新）");
        Assert.Equal("第二本", viewModel.SelectedBookTitle);
        Assert.Equal([0], viewModel.SelectedChapterIndices);
        Assert.True(viewModel.Books.Single(book => book.BookId == "book-2").IsSelected);
    }

    private async Task Catalog_reconciliation_preserves_selection_and_only_removes_missing_chapters()
    {
        var workspaceService = new FakeCacheWorkspaceService
        {
            BooksResult = [new CachedBookCacheItem("book-1", "第一本", null, 5, 5, 5120)]
        };
        workspaceService.ChaptersResult["book-1"] = Enumerable.Range(0, 6)
            .Select(index => new CachedChapterCacheItem(
                "book-1",
                index,
                $"第 {index + 1} 章",
                1,
                1,
                1024,
                1))
            .ToArray();
        var viewModel = CreateViewModel(workspaceService);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        foreach (var chapterIndex in new[] { 1, 3, 5 })
        {
            viewModel.HandleChapterClick(
                viewModel.Chapters.Single(chapter => chapter.ChapterIndex == chapterIndex),
                viewModel.SelectedChapterIndices.Count == 0
                    ? DesktopSelectionModifiers.None
                    : DesktopSelectionModifiers.Control);
        }

        var collectionChanges = new List<NotifyCollectionChangedAction>();
        viewModel.Chapters.CollectionChanged += (_, args) => collectionChanges.Add(args.Action);
        workspaceService.ChaptersResult["book-1"] =
        [
            .. workspaceService.ChaptersResult["book-1"],
            new CachedChapterCacheItem("book-1", 10, "第 11 章", 1, 1, 1024, 1)
        ];
        workspaceService.Publish(CacheInvalidation.ForChapters(
            "book-1",
            [10],
            CacheInvalidationAspect.PhysicalSummary |
            CacheInvalidationAspect.CatalogStructure |
            CacheInvalidationAspect.Coverage));

        Assert.Equal([1, 3, 5], viewModel.SelectedChapterIndices);
        var addedChapter = viewModel.Chapters.Single(chapter => chapter.ChapterIndex == 10);
        Assert.False(addedChapter.IsSelected);
        Assert.Equal("1 条缓存", addedChapter.EntryCountText);
        Assert.DoesNotContain("配置不可用", addedChapter.CompletenessText, StringComparison.Ordinal);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, collectionChanges);

        workspaceService.ChaptersResult["book-1"] = workspaceService.ChaptersResult["book-1"]
            .Where(chapter => chapter.ChapterIndex != 3)
            .ToArray();
        workspaceService.Publish(CacheInvalidation.ForChapters(
            "book-1",
            [3],
            CacheInvalidationAspect.PhysicalSummary |
            CacheInvalidationAspect.CatalogStructure |
            CacheInvalidationAspect.Coverage));

        Assert.Equal([1, 5], viewModel.SelectedChapterIndices);
        Assert.DoesNotContain(viewModel.Chapters, chapter => chapter.ChapterIndex == 3);
    }

    private async Task Page_leave_cancels_pending_cache_refresh_and_discards_late_results()
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

    private async Task Switching_books_clears_chapter_selection_without_cross_book_carryover()
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

    private async Task Clear_selected_chapters_uses_one_application_batch_request()
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

    private async Task Selecting_all_chapters_cleans_the_whole_visible_book_through_batch_boundary()
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

    private async Task Clearing_the_last_cached_book_clears_catalog_and_selection_state()
    {
        var workspaceService = CreateTwoBookWorkspace();
        workspaceService.ChaptersResult["book-1"] =
        [new CachedChapterCacheItem("book-1", 0, "第一章", 1, 1, 1024, 1)];
        var viewModel = CreateViewModel(workspaceService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);
        workspaceService.BooksSequence = [[]];

        await viewModel.ClearSelectedChaptersCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Chapters);
        Assert.Empty(viewModel.SelectedChapterIndices);
        Assert.False(viewModel.HasSelection);
        Assert.False(viewModel.SelectedBookHasCache);
        Assert.Empty(viewModel.SelectedBookTitle);
        Assert.False(viewModel.TryHandleEscape());
    }

    private async Task Export_command_is_enabled_for_any_selection_and_keeps_unavailable_reasons_accessible()
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

    private async Task Mixed_selection_cancel_does_not_open_folder_or_start_background_export()
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

    private async Task Navigating_from_during_skip_confirmation_cancels_only_page_owned_preparation()
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

    private async Task Mixed_selection_confirm_submits_only_exportable_chapters_to_background_coordinator()
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

    private async Task All_unavailable_selection_warns_without_confirmation_folder_or_background_export()
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

    private async Task Export_directory_cancellation_does_not_start_background_export()
    {
        var coordinator = new FakeChapterExportCoordinator();
        var folders = new FakePresentationFileDialogService { FolderResult = null };
        var viewModel = await CreateExportReadyViewModelAsync(coordinator, folders);

        await viewModel.ExportSelectedChaptersCommand.ExecuteAsync(null);

        Assert.Equal(0, coordinator.StartCallCount);
    }

    private async Task Export_submits_frozen_selection_and_destination_to_background_coordinator()
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

    private async Task Active_background_export_disables_new_export_without_blocking_cleanup_selection_state()
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

    private async Task Background_export_completion_reenables_export_while_page_is_active()
    {
        var coordinator = new FakeChapterExportCoordinator(CreateExportSnapshot(ChapterExportBatchStatus.Running));
        var viewModel = await CreateExportReadyViewModelAsync(
            coordinator,
            new FakePresentationFileDialogService { FolderResult = @"D:\Export" });

        coordinator.Publish(CreateExportSnapshot(ChapterExportBatchStatus.Completed));

        Assert.True(viewModel.CanExportSelectedChapters);
        Assert.True(viewModel.ExportSelectedChaptersCommand.CanExecute(null));
    }

    private async Task Navigating_from_page_does_not_cancel_already_started_background_export()
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

    [Fact]
    public async Task Cache_management_selection_and_projection_contracts_cover_loading_status_and_cleanup()
    {
        await LoadAsync_does_not_auto_select_first_book();
        await Superseded_book_load_does_not_report_a_stale_error();
        await SelectBookAsync_ignores_late_results_from_previous_selection();
        await Chapter_selection_uses_desktop_modifiers_select_all_and_clear();
        await Coverage_only_invalidation_refreshes_current_configuration_status();
        await Loading_a_10000_chapter_cache_catalog_avoids_item_by_item_add_notifications();
        await Chapter_card_marks_current_configuration_completeness_as_unavailable();
        await Chapter_cards_project_current_configuration_statuses_without_turning_zero_zero_into_full();
        await Matching_cache_changes_are_coalesced_and_refresh_only_during_page_activation();
        await Matching_cache_changes_refresh_only_the_targeted_book_summary();
        await Reentering_cache_management_does_not_duplicate_cache_change_subscription();
        await Invalidation_refreshes_another_book_without_disturbing_current_selection();
        await Catalog_reconciliation_preserves_selection_and_only_removes_missing_chapters();
        await Page_leave_cancels_pending_cache_refresh_and_discards_late_results();
        await Switching_books_clears_chapter_selection_without_cross_book_carryover();
        await Clear_selected_chapters_uses_one_application_batch_request();
        await Selecting_all_chapters_cleans_the_whole_visible_book_through_batch_boundary();
        await Clearing_the_last_cached_book_clears_catalog_and_selection_state();
    }

    private async Task Superseded_book_load_does_not_report_a_stale_error()
    {
        var workspaceService = new FakeCacheWorkspaceService();
        var feedback = new FakeFeedbackService();
        var pendingBooks = new TaskCompletionSource<IReadOnlyList<CachedBookCacheItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        workspaceService.PendingBooksTask = pendingBooks;
        var viewModel = CreateViewModel(workspaceService, feedback);

        var load = viewModel.LoadAsync(CancellationToken.None);
        await workspaceService.BooksLoadStarted.Task;
        viewModel.HandleNavigatedFrom();
        pendingBooks.SetException(new InvalidOperationException("旧请求失败"));

        await load;

        Assert.Null(feedback.LastTitle);
    }

    [Fact]
    public async Task Cache_management_export_preparation_contracts_cover_selection_and_cancellation()
    {
        await Export_command_is_enabled_for_any_selection_and_keeps_unavailable_reasons_accessible();
        await Mixed_selection_cancel_does_not_open_folder_or_start_background_export();
        await Navigating_from_during_skip_confirmation_cancels_only_page_owned_preparation();
        await Mixed_selection_confirm_submits_only_exportable_chapters_to_background_coordinator();
        await All_unavailable_selection_warns_without_confirmation_folder_or_background_export();
        await Export_directory_cancellation_does_not_start_background_export();
    }

    [Fact]
    public async Task Cache_management_export_submission_contracts_freeze_destination_and_selection()
    {
        await Export_submits_frozen_selection_and_destination_to_background_coordinator();
    }

    [Fact]
    public async Task Cache_management_background_export_contracts_cover_busy_state_completion_and_leave()
    {
        await Active_background_export_disables_new_export_without_blocking_cleanup_selection_state();
        await Background_export_completion_reenables_export_while_page_is_active();
        await Navigating_from_page_does_not_cancel_already_started_background_export();
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
            new FakeCacheCatalog(workspaceService),
            new FakeCacheCoverageQuery(workspaceService),
            workspaceService.InvalidationCoordinator,
            feedbackService ?? new FakeFeedbackService(),
            dialogService ?? new FakeAppDialogService(),
            new FakeNavigationService(),
            coordinator ?? new FakeChapterExportCoordinator(),
            fileDialogs ?? new FakePresentationFileDialogService(),
            new InlineUiScheduler());
    }

    private sealed class FakeCacheCatalog(FakeCacheWorkspaceService workspace) : ICacheCatalog
    {
        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(CancellationToken cancellationToken) =>
            (await workspace.GetCachedBooksAsync(cancellationToken))
                .Select(book => new CachedBookSummary(
                    book.BookId,
                    book.Title,
                    book.Author,
                    book.ChapterCount,
                    book.EntryCount,
                    book.TotalSizeBytes))
                .ToArray();

        public async Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(
            IReadOnlyCollection<string> bookIds,
            CancellationToken cancellationToken) =>
            (await GetCachedBooksAsync(cancellationToken))
                .Where(book => bookIds.Contains(book.BookId, StringComparer.Ordinal))
                .ToArray();

        public async Task<CachedBookSummary?> GetCachedBookAsync(string bookId, CancellationToken cancellationToken) =>
            (await workspace.GetCachedBookAsync(bookId, cancellationToken)) is { } book
                ? new CachedBookSummary(book.BookId, book.Title, book.Author, book.ChapterCount, book.EntryCount, book.TotalSizeBytes)
                : null;

        public async Task<IReadOnlyList<CachedChapterCatalogEntry>> GetCachedChapterCatalogAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            (await workspace.GetCachedChaptersAsync(bookId, cancellationToken))
                .Select(chapter => new CachedChapterCatalogEntry(chapter.BookId, chapter.ChapterIndex, chapter.Title))
                .ToArray();

        public async Task<IReadOnlyList<CachedChapterSummary>> GetCachedChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            (await workspace.GetCachedChaptersAsync(bookId, cancellationToken))
                .Where(chapter => chapterIndices.Contains(chapter.ChapterIndex))
                .Select(chapter => new CachedChapterSummary(
                    chapter.BookId,
                    chapter.ChapterIndex,
                    chapter.Title,
                    chapter.CachedSegmentCount,
                    chapter.EntryCount,
                    chapter.TotalSizeBytes))
                .ToArray();

        public async Task<CachedChapterSummary?> GetCachedChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            (await workspace.GetCachedChapterAsync(bookId, chapterIndex, cancellationToken)) is { } chapter
                ? new CachedChapterSummary(
                    chapter.BookId,
                    chapter.ChapterIndex,
                    chapter.Title,
                    chapter.CachedSegmentCount,
                    chapter.EntryCount,
                    chapter.TotalSizeBytes)
                : null;
    }

    private sealed class FakeCacheCoverageQuery(FakeCacheWorkspaceService workspace) : ICacheCoverageQuery
    {
        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            workspace.CoverageQueryCallCount++;
            return Task.FromResult<IReadOnlyList<ChapterCacheStatus>>(
                (workspace.ChaptersResult.TryGetValue(bookId, out var chapters) ? chapters : [])
                .Where(chapter => chapterIndices.Contains(chapter.ChapterIndex))
                .Select(chapter => new ChapterCacheStatus(
                    chapter.ChapterIndex,
                    workspace.CoverageCachedSegmentCounts.GetValueOrDefault(
                        (bookId, chapter.ChapterIndex),
                        chapter.CachedSegmentCount),
                    chapter.CurrentConfigurationSegmentCount)
                {
                    Kind = chapter.CurrentConfigurationStatus
                })
                .ToArray());
        }

        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            IReadOnlyCollection<NovelSpeaker.Application.Playback.PlaybackChapterMetadata> chapters,
            CancellationToken cancellationToken) =>
            GetAsync(bookId, chapterIndices, cancellationToken);
    }

    private sealed class FakeCacheInvalidationCoordinator : ICacheInvalidationCoordinator
    {
        private EventHandler<CacheInvalidationBatch>? _batchPublished;

        public event EventHandler<CacheInvalidationBatch>? BatchPublished
        {
            add
            {
                _batchPublished += value;
                SubscriberCount++;
            }
            remove
            {
                _batchPublished -= value;
                SubscriberCount--;
            }
        }

        public int SubscriberCount { get; private set; }

        public void Publish(CacheInvalidation invalidation) =>
            _batchPublished?.Invoke(this, new CacheInvalidationBatch([invalidation]));

        public Task FlushPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

        public FakeCacheInvalidationCoordinator InvalidationCoordinator { get; } = new();

        public int InvalidationSubscriberCount => InvalidationCoordinator.SubscriberCount;

        public IReadOnlyList<CachedBookCacheItem> BooksResult { get; set; } = [];

        public TaskCompletionSource<IReadOnlyList<CachedBookCacheItem>>? PendingBooksTask { get; set; }

        public TaskCompletionSource BooksLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        public Dictionary<(string BookId, int ChapterIndex), int> CoverageCachedSegmentCounts { get; } = [];

        public Dictionary<string, TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>> PendingChapterTasks { get; } = [];

        public Queue<TaskCompletionSource<IReadOnlyList<CachedChapterCacheItem>>> PendingChapterSequences { get; } = [];

        public TaskCompletionSource FirstPendingChapterLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ChangedSubscriberCount { get; private set; }

        public int GetCachedChaptersCallCount { get; private set; }

        public int GetCachedChapterCallCount { get; private set; }

        public int CoverageQueryCallCount { get; set; }

        public int GetCachedBookCallCount { get; private set; }

        public int GetCachedBooksCallCount { get; private set; }

        public bool LoadChaptersOnBackgroundThread { get; set; }

        private TaskCompletionSource? _chapterRefreshCompleted;
        private int _chapterRefreshCompletionTarget;
        private TaskCompletionSource? _bookLoadCompleted;
        private int _bookLoadCompletionTarget;

        public CacheCleanupResult ClearBookResult { get; set; } = new(0, 0, 0, 0);

        public CacheCleanupResult ClearChaptersResult { get; set; } = new(0, 0, 0, 0);

        public (string BookId, int[] ChapterIndices)? LastClearChaptersRequest { get; private set; }

        public int ClearChaptersCallCount { get; private set; }

        public int ClearBookCallCount { get; private set; }

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken)
        {
            GetCachedBooksCallCount++;
            if (PendingBooksTask is not null)
            {
                BooksLoadStarted.TrySetResult();
                var pendingBooks = await PendingBooksTask.Task;
                SignalBookLoadCompleted();
                return pendingBooks;
            }

            if (_booksQueue.Count > 0)
            {
                BooksResult = _booksQueue.Dequeue();
            }

            SignalBookLoadCompleted();
            return BooksResult;
        }

        public Task<CachedBookCacheItem?> GetCachedBookAsync(
            string bookId,
            CancellationToken cancellationToken)
        {
            GetCachedBookCallCount++;
            if (_booksQueue.Count > 0)
            {
                BooksResult = _booksQueue.Dequeue();
            }

            return Task.FromResult(BooksResult.FirstOrDefault(book => book.BookId == bookId));
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

        public async Task<CachedChapterCacheItem?> GetCachedChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            GetCachedChapterCallCount++;
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

                SignalChapterRefreshCompleted();
                return chapterItems.FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex);
            }

            var chapters = ChaptersResult.TryGetValue(bookId, out var result) ? result : [];
            SignalChapterRefreshCompleted();
            return chapters.FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex);
        }

        public Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChapterDecorationsAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            var requested = chapterIndices.ToHashSet();
            var chapters = ChaptersResult.TryGetValue(bookId, out var result) ? result : [];
            return Task.FromResult<IReadOnlyList<CachedChapterCacheItem>>(
                chapters.Where(chapter => requested.Contains(chapter.ChapterIndex)).ToArray());
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

        public Task WhenChapterRefreshCountReached(int count)
        {
            if (GetCachedChapterCallCount >= count)
            {
                return Task.CompletedTask;
            }

            _chapterRefreshCompleted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _chapterRefreshCompletionTarget = count;
            return _chapterRefreshCompleted.Task;
        }

        public Task WhenBookLoadCountReached(int count)
        {
            if (GetCachedBooksCallCount >= count)
            {
                return Task.CompletedTask;
            }

            _bookLoadCompleted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _bookLoadCompletionTarget = count;
            return _bookLoadCompleted.Task;
        }

        public void Publish(CacheChangedEventArgs eventArgs)
        {
            _changed?.Invoke(this, eventArgs);
            var invalidation = eventArgs.BookId is null
                ? CacheInvalidation.ForGlobal(CacheInvalidationAspect.PhysicalSummary | CacheInvalidationAspect.CatalogStructure)
                : eventArgs.ChapterIndex is int chapterIndex
                    ? CacheInvalidation.ForChapters(
                        eventArgs.BookId,
                        [chapterIndex],
                        CacheInvalidationAspect.PhysicalSummary |
                        CacheInvalidationAspect.Coverage)
                    : CacheInvalidation.ForBook(
                        eventArgs.BookId,
                        CacheInvalidationAspect.PhysicalSummary |
                        CacheInvalidationAspect.CatalogStructure);
            InvalidationCoordinator.Publish(invalidation);
        }

        public void Publish(CacheInvalidation invalidation) => InvalidationCoordinator.Publish(invalidation);

        private void SignalChapterLoadCompleted()
        {
            if (GetCachedChaptersCallCount >= _chapterLoadCompletionTarget)
            {
                _chapterLoadCompleted?.TrySetResult();
            }
        }

        private void SignalChapterRefreshCompleted()
        {
            if (GetCachedChapterCallCount >= _chapterRefreshCompletionTarget)
            {
                _chapterRefreshCompleted?.TrySetResult();
            }
        }

        private void SignalBookLoadCompleted()
        {
            if (GetCachedBooksCallCount >= _bookLoadCompletionTarget)
            {
                _bookLoadCompleted?.TrySetResult();
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
            if (ChaptersResult.TryGetValue(bookId, out var chapters))
            {
                ChaptersResult[bookId] = chapters
                    .Where(chapter => !chapterIndices.Contains(chapter.ChapterIndex))
                    .ToArray();
            }

            InvalidationCoordinator.Publish(
                CacheInvalidation.ForChapters(
                    bookId,
                    chapterIndices,
                    CacheInvalidationAspect.PhysicalSummary |
                    CacheInvalidationAspect.CatalogStructure |
                    CacheInvalidationAspect.Coverage));
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

    private sealed class FakeNavigationService : IAppNavigator
    {
        public AppRoute CurrentRoute => AppRoutes.Library;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(false);

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(true);
    }

    private sealed class InlineUiScheduler : IUiScheduler
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
}
