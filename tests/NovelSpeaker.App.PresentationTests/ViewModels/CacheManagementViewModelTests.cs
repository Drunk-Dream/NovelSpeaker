using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.Export;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Cache;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class CacheManagementViewModelTests
{
    [Fact]
    public async Task Returning_to_page_rejects_old_selected_book_failure_and_drains_all_page_work()
    {
        var fixture = CreateCache();
        var oldCatalog = new TaskCompletionSource<CacheReadResult<CachedBookCatalog>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ReadModel.BookHandler = (_, _) => oldCatalog.Task;
        using var owner = new PageActivationController();
        var oldActivation = owner.Activate();
        var viewModel = CreateViewModel(fixture);
        viewModel.HandleNavigatedTo(oldActivation);
        await viewModel.LoadAsync(oldActivation.CancellationToken);
        var oldSelection = viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

        owner.Deactivate();
        Assert.Equal(0, fixture.ReadModel.SubscriberCount);
        var next = owner.Activate();
        viewModel.HandleNavigatedTo(next);
        var load = viewModel.LoadAsync(next.CancellationToken);
        fixture.ReadModel.BookHandler = null;
        oldCatalog.SetException(new IOException("late catalog failure"));
        await Task.WhenAll(oldSelection, load);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        await oldActivation.WaitForPendingOperationsAsync();
        await next.WaitForPendingOperationsAsync();
        Assert.Equal(2, viewModel.Chapters.Count);
        Assert.False(viewModel.IsLoadingChapters);
        Assert.True(viewModel.HasSelection);
        Assert.Null(fixture.Feedback.LastTitle);
        Assert.Equal(1, fixture.ReadModel.SubscriberCount);
    }

    [Fact]
    public async Task Replaced_decoration_window_failure_is_silent_and_latest_window_is_committed()
    {
        var fixture = CreateCache(chapterCount: 64);
        var pending = new TaskCompletionSource<CacheReadResult<IReadOnlyList<CacheChapterView>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ReadModel.ChapterHandler = (_, _, _) => pending.Task;
        using var owner = new PageActivationController();
        var activation = owner.Activate();
        var viewModel = CreateViewModel(fixture);
        viewModel.HandleNavigatedTo(activation);
        await viewModel.LoadAsync(activation.CancellationToken);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.RequestChapterDecorationWindow(32, 32);
        fixture.ReadModel.ChapterHandler = null;
        pending.SetException(new IOException("old window failure"));
        await activation.WaitForPendingOperationsAsync();
        Assert.Null(fixture.Feedback.LastTitle);
        Assert.Equal(Enumerable.Range(32, 32), fixture.ReadModel.LastRequestedChapterIndices);
        Assert.Equal("1 条缓存", viewModel.Chapters[32].EntryCountText);
        owner.Deactivate();
        Assert.Equal(0, fixture.ReadModel.SubscriberCount);
    }

    [Fact]
    public async Task Switching_to_removed_book_during_background_projection_clears_actual_projected_selection()
    {
        var fixture = CreateCache();
        fixture.ReadModel.Books = [.. fixture.ReadModel.Books, new("book-2", "Other", null, 1, 1, 100),
            .. Enumerable.Range(0, 998).Select(index => new CachedBookSummary($"other-{index:D5}", "Other", null, 1, 1, 100))];
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        var removedBook = viewModel.Books[1];
        var books = new PausingBookList(fixture.ReadModel.Books.Where(book => book.BookId != "book-2")
            .Select(book => book with { Title = "Updated " + book.Title }).ToArray());
        fixture.ReadModel.Books = books;
        fixture.ReadModel.Publish(new CacheReadModelScope.Global());
        await books.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task selection;
        try
        {
            selection = viewModel.SelectBookCommand.ExecuteAsync(removedBook);
        }
        finally
        {
            books.Release.TrySetResult();
        }
        await selection;
        Assert.False(viewModel.HasSelection);
        Assert.Empty(viewModel.Chapters);
        Assert.DoesNotContain(viewModel.Books, book => book.IsSelected);
        Assert.Equal(999, viewModel.Books.Count);
    }

    [Fact]
    public async Task Queued_book_load_requeries_window_after_notification_already_projected_selected_book()
    {
        var fixture = CreateCache();
        fixture.ReadModel.Books = [.. fixture.ReadModel.Books, new("book-2", "Other", null, 1, 1, 100),
            .. Enumerable.Range(0, 998).Select(index => new CachedBookSummary($"other-{index:D5}", "Other", null, 1, 1, 100))];
        fixture.ReadModel.ChaptersByBook["book-2"] = [new(0, new("book-2", 0, "Other chapter", 1, 1, 100), new(0, 1, 1))];
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        var otherBook = viewModel.Books[1];
        var books = new PausingBookList(fixture.ReadModel.Books.Select(book => book with { Title = "Updated " + book.Title }).ToArray());
        fixture.ReadModel.Books = books;
        fixture.ReadModel.Publish(new CacheReadModelScope.Global());
        await books.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task selection;
        try
        {
            selection = viewModel.SelectBookCommand.ExecuteAsync(otherBook);
        }
        finally
        {
            books.Release.TrySetResult();
        }
        await selection;
        var chapter = Assert.Single(viewModel.Chapters);
        Assert.Equal("Other chapter", chapter.Title);
        Assert.True(chapter.IsExportable);
        Assert.Contains("100%", chapter.CompletenessText);
        Assert.Equal("book-2", Assert.Single(viewModel.Books, book => book.IsSelected).BookId);
    }

    [Fact]
    public async Task Click_during_staged_book_reordering_cannot_inject_old_positions_into_new_catalog()
    {
        var fixture = CreateCache();
        fixture.ReadModel.Books = [.. fixture.ReadModel.Books, .. Enumerable.Range(0, 999)
            .Select(index => new CachedBookSummary($"other-{index:D5}", "Other", null, 1, 1, 100))];
        var scheduler = new PausingUiScheduler();
        var viewModel = CreateViewModel(fixture, scheduler);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        var oldPositionBook = viewModel.Books[500];
        scheduler.PauseNextLater = true;
        fixture.ReadModel.Books = fixture.ReadModel.Books.Select((book, index) => book with
        {
            Title = "Updated " + book.Title,
            TotalSizeBytes = 10_000 + index
        }).ToArray();
        fixture.ReadModel.Publish(new CacheReadModelScope.Global());
        await scheduler.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.True(viewModel.IsLoadingBooks);
            await viewModel.SelectBookCommand.ExecuteAsync(oldPositionBook);
        }
        finally
        {
            scheduler.Release.TrySetResult();
        }
        await AwaitStateAsync(viewModel, () => !viewModel.IsLoadingBooks && viewModel.Books.Count == 1000 &&
                viewModel.SelectedBookTitle == "Updated 第一本", () => { });
        Assert.Equal(1000, viewModel.Books.Select(book => book.BookId).Distinct().Count());
        var selected = Assert.Single(viewModel.Books, book => book.IsSelected);
        Assert.Equal("book-1", selected.BookId);
        Assert.Same(selected, viewModel.Books[^1]);
        Assert.Equal([0, 1], viewModel.Chapters.Select(chapter => chapter.ChapterIndex));
    }

    [Fact]
    public async Task Large_global_book_change_uses_staged_projection_and_preserves_selected_book()
    {
        var fixture = CreateCache();
        fixture.ReadModel.Books = [.. fixture.ReadModel.Books, .. Enumerable.Range(0, 999)
            .Select(index => new CachedBookSummary($"other-{index:D5}", "Other", null, 1, 1, 100))];
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        viewModel.Books.CollectionChanged += (_, args) => actions.Add(args.Action);
        fixture.ReadModel.Books = fixture.ReadModel.Books.Select(book => book with { Title = "Updated " + book.Title }).ToArray();
        await AwaitStateAsync(viewModel, () => viewModel.Books.Count == 1000 && viewModel.Books[0].IsSelected &&
                viewModel.SelectedBookTitle == "Updated 第一本",
            () => fixture.ReadModel.Publish(new CacheReadModelScope.Global()));
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Add, actions);
        Assert.Equal([0], viewModel.SelectedChapterIndices);
    }

    [Fact]
    public async Task Global_configuration_change_keeps_large_book_list_projection_and_selection()
    {
        var fixture = CreateCache();
        fixture.ReadModel.Books = [.. fixture.ReadModel.Books, .. Enumerable.Range(0, 9999)
            .Select(index => new CachedBookSummary($"other-{index:D5}", "Other", null, 1, 1, 100))];
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        var first = viewModel.Books[0];
        var middle = viewModel.Books[5000];
        var last = viewModel.Books[^1];
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        viewModel.Books.CollectionChanged += (_, args) => actions.Add(args.Action);
        var catalogRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ReadModel.BookHandler = (bookId, _) =>
        {
            catalogRequested.TrySetResult();
            return Task.FromResult(new CacheReadResult<CachedBookCatalog>(fixture.ReadModel.Revision,
                new(fixture.ReadModel.Books[0], fixture.ReadModel.ChaptersByBook[bookId]
                    .Select(view => new CachedChapterCatalogEntry(bookId, view.ChapterIndex, view.Physical!.Title)).ToArray())));
        };
        fixture.ReadModel.Publish(new CacheReadModelScope.Global());
        // The selected catalog query starts after the entire book projection has committed.
        await catalogRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(actions);
        Assert.Same(first, viewModel.Books[0]);
        Assert.Same(middle, viewModel.Books[5000]);
        Assert.Same(last, viewModel.Books[^1]);
        Assert.True(viewModel.Books[0].IsSelected);
        Assert.True(viewModel.HasSelection);
    }

    [Fact]
    public async Task Book_summary_reordering_preserves_selected_book_and_identity_mapping()
    {
        var fixture = CreateCache();
        fixture.ReadModel.Books = [.. fixture.ReadModel.Books, new("book-2", "Other", null, 1, 1, 100)];
        fixture.ReadModel.ChaptersByBook["book-2"] = [new(0, new("book-2", 0, "Other chapter", 1, 1, 100), new(0, 1, 1))];
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        fixture.ReadModel.Books = [fixture.ReadModel.Books[0], fixture.ReadModel.Books[1] with { TotalSizeBytes = 4096 }];
        await AwaitStateAsync(viewModel, () => viewModel.Books[0].BookId == "book-2" && viewModel.Books[1].IsSelected,
            () => fixture.ReadModel.Publish(new CacheReadModelScope.Book("book-2")));
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        Assert.True(viewModel.Books[0].IsSelected);
        Assert.False(viewModel.Books[1].IsSelected);
        Assert.Equal("Other chapter", Assert.Single(viewModel.Chapters).Title);
    }

    [Fact]
    public async Task Chapter_changes_update_only_affected_rows_and_keep_zero_percent_and_selection()
    {
        var fixture = CreateCache(chapterCount: 10_000);
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.RequestChapterDecorationWindow(5000, 32);
        viewModel.HandleChapterClick(viewModel.Chapters[5001], DesktopSelectionModifiers.None);
        var unchanged = viewModel.Chapters[5001];
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        viewModel.Chapters.CollectionChanged += (_, args) => actions.Add(args.Action);
        var current = fixture.ReadModel.ChaptersByBook["book-1"].ToArray();
        current[5000] = current[5000] with { Coverage = new(5000, 0, 4) };
        fixture.ReadModel.ChaptersByBook["book-1"] = current;
        await AwaitStateAsync(viewModel, () => viewModel.Chapters[5000].CompletenessText.Contains("0/4"),
            () => fixture.ReadModel.Publish(new CacheReadModelScope.Chapters("book-1", [5000])));

        Assert.Same(unchanged, viewModel.Chapters[5001]);
        Assert.Equal([5001], viewModel.SelectedChapterIndices);
        Assert.Contains("0%", viewModel.Chapters[5000].CompletenessText);
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, actions);
        Assert.Equal([5000], fixture.ReadModel.ChapterQueries[^1].Indices);
        Assert.All(fixture.ReadModel.ChapterQueries, query => Assert.InRange(query.Indices.Count, 1, 32));
    }

    [Fact]
    public async Task Chapter_cleanup_and_addition_reconcile_catalog_without_resetting_valid_selection()
    {
        var fixture = CreateCache(chapterCount: 3);
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);
        viewModel.HandleChapterClick(viewModel.Chapters[1], DesktopSelectionModifiers.Control);
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        viewModel.Chapters.CollectionChanged += (_, args) => actions.Add(args.Action);
        fixture.ReadModel.ChaptersByBook["book-1"] =
        [fixture.ReadModel.ChaptersByBook["book-1"][0], fixture.ReadModel.ChaptersByBook["book-1"][2],
            new(3, new("book-1", 3, "New chapter", 1, 1, 1024), new(3, 1, 1))];
        await AwaitStateAsync(viewModel, () =>
                viewModel.Chapters.Select(chapter => chapter.ChapterIndex).SequenceEqual([0, 2, 3]) &&
                viewModel.SelectedChapterIndices.SequenceEqual([0]),
            () => fixture.ReadModel.Publish(new CacheReadModelScope.Chapters("book-1", [1, 3])));

        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, actions);
        Assert.True(viewModel.Chapters[0].IsSelected);
    }

    [Fact]
    public async Task Removing_selected_cached_book_clears_selection_and_leaving_unsubscribes()
    {
        var fixture = CreateCache();
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        Assert.Equal(1, fixture.ReadModel.SubscriberCount);
        fixture.ReadModel.Books = [];
        await AwaitStateAsync(viewModel, () => !viewModel.HasSelection && viewModel.Chapters.Count == 0,
            () => fixture.ReadModel.Publish(new CacheReadModelScope.Book("book-1")));
        Assert.Empty(viewModel.Books);
        viewModel.HandleNavigatedFrom();
        Assert.Equal(0, fixture.ReadModel.SubscriberCount);
        var requests = fixture.ReadModel.ChapterQueries.Count;
        fixture.ReadModel.Publish(new CacheReadModelScope.Global());
        Assert.Equal(requests, fixture.ReadModel.ChapterQueries.Count);
    }

    [Fact]
    public async Task Unrelated_book_changes_do_not_requery_selected_chapters()
    {
        var fixture = CreateCache();
        fixture.ReadModel.Books = [.. fixture.ReadModel.Books, new("book-2", "Other", null, 1, 1, 100)];
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        var chapter = viewModel.Chapters[0];
        var requests = fixture.ReadModel.ChapterQueries.Count;
        fixture.ReadModel.Books = [fixture.ReadModel.Books[0], fixture.ReadModel.Books[1] with { Title = "Updated" }];
        await AwaitStateAsync(viewModel, () => viewModel.Books[1].Title == "Updated",
            () => fixture.ReadModel.Publish(new CacheReadModelScope.Book("book-2")));
        Assert.Same(chapter, viewModel.Chapters[0]);
        Assert.Equal(requests, fixture.ReadModel.ChapterQueries.Count);
    }

    [Fact]
    public async Task Book_scope_updates_catalog_and_coverage_preserving_surviving_chapter_selection()
    {
        var fixture = CreateCache();
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);
        fixture.ReadModel.ChaptersByBook["book-1"] = fixture.ReadModel.ChaptersByBook["book-1"].Select(view => view with
        {
            Physical = view.Physical! with { Title = "Updated chapter" },
            Coverage = new(view.ChapterIndex, 2, 2)
        }).ToArray();
        await AwaitStateAsync(viewModel, () => viewModel.Chapters[0].Title == "Updated chapter" && viewModel.Chapters[0].IsExportable,
            () => fixture.ReadModel.Publish(new CacheReadModelScope.Book("book-1")));
        Assert.Equal([0], viewModel.SelectedChapterIndices);
    }

    [Fact]
    public async Task Old_activation_catalog_result_cannot_write_after_leaving_and_returning()
    {
        var fixture = CreateCache();
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        var response = new TaskCompletionSource<CacheReadResult<CachedBookCatalog>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ReadModel.BookHandler = (_, _) => response.Task;
        var selection = viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleNavigatedFrom();
        fixture.ReadModel.BookHandler = null;
        var activation = viewModel.LoadAsync(CancellationToken.None);
        response.SetResult(new(0, new(fixture.ReadModel.Books[0], [new("book-1", 99, "Old chapter")])));
        await selection;
        await activation;
        Assert.False(viewModel.HasSelection);
        Assert.Empty(viewModel.Chapters);
        Assert.Null(fixture.Feedback.LastTitle);
    }

    [Fact]
    public async Task Late_window_query_cannot_overwrite_newer_committed_chapter_view()
    {
        var fixture = CreateCache();
        var viewModel = CreateViewModel(fixture);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        var response = new TaskCompletionSource<CacheReadResult<IReadOnlyList<CacheChapterView>>>();
        var old = fixture.ReadModel.ChaptersByBook["book-1"][1];
        fixture.ReadModel.ChapterHandler = (_, _, _) => response.Task;
        // Starting on a worker leaves no SynchronizationContext; completing the controlled
        // response below runs this query continuation synchronously, without timing guesses.
        await Task.Run(() => viewModel.RequestChapterDecorationWindow(1, 1));
        fixture.ReadModel.ChapterHandler = null;
        fixture.ReadModel.ChaptersByBook["book-1"] =
        [fixture.ReadModel.ChaptersByBook["book-1"][0], old with { Coverage = new(1, 2, 2) }];
        await AwaitStateAsync(viewModel, () => viewModel.Chapters[1].IsExportable,
            () => fixture.ReadModel.Publish(new CacheReadModelScope.Chapters("book-1", [1])));
        await Task.Run(() => response.SetResult(new(0, [old])));
        Assert.True(viewModel.Chapters[1].IsExportable);
        Assert.Contains("100%", viewModel.Chapters[1].CompletenessText);
    }

    private static async Task AwaitStateAsync(CacheManagementViewModel viewModel, Func<bool> ready, Action trigger)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check() { if (ready()) completion.TrySetResult(); }
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => Check();
        viewModel.PropertyChanged += Changed;
        try
        {
            trigger();
            Check();
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            viewModel.PropertyChanged -= Changed;
        }
    }

    [Fact]
    public async Task Loading_and_selecting_book_projects_catalog_and_current_coverage()
    {
        var fixture = CreateCache();
        var viewModel = CreateViewModel(fixture);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.True(viewModel.Books[0].IsSelected);
        Assert.True(viewModel.HasSelection);
        Assert.Equal("第一本", viewModel.SelectedBookTitle);
        Assert.Equal(["第 1 章", "第 2 章"], viewModel.Chapters.Select(chapter => chapter.Title));
        Assert.Equal("完整度：1/2 段 · 50%", viewModel.Chapters[0].CompletenessText);
    }

    [Fact]
    public async Task Selecting_and_clearing_chapters_uses_store_batch_boundary()
    {
        var fixture = CreateCache();
        var viewModel = CreateViewModel(fixture);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);

        await viewModel.ClearSelectedChaptersCommand.ExecuteAsync(null);

        Assert.Equal(("book-1", new[] { 0 }), fixture.Store.LastClearChaptersRequest);
        Assert.Equal(1, fixture.Store.ClearChaptersCallCount);
        Assert.Equal("缓存已清理", fixture.Feedback.LastTitle);
    }

    [Fact]
    public async Task Large_catalog_is_replaced_as_one_collection_projection()
    {
        var fixture = CreateCache(chapterCount: 10_000);
        var viewModel = CreateViewModel(fixture);
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        viewModel.Chapters.CollectionChanged += (_, args) => actions.Add(args.Action);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);

        Assert.Equal(10_000, viewModel.Chapters.Count);
        Assert.DoesNotContain(
            System.Collections.Specialized.NotifyCollectionChangedAction.Add,
            actions);
        Assert.Contains(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset,
            actions);
    }

    private static CacheFixture CreateCache(int chapterCount = 2)
    {
        var chapters = Enumerable.Range(0, chapterCount)
            .Select(index => new CachedChapterSummary(
                "book-1",
                index,
                $"第 {index + 1} 章",
                1,
                1,
                1024))
            .ToArray();
        var statuses = chapters
            .Select(chapter => new ChapterCacheStatus(
                chapter.ChapterIndex,
                chapter.ChapterIndex == 0 ? 1 : 0,
                2))
            .ToArray();
        var store = new CacheStoreTestDouble
        {
            StoreSummary = new AudioCacheStoreSummary(
                chapterCount * 1024L,
                chapterCount,
                AppSettings.DefaultCacheLimitBytes,
                false),
            CleanupResult = new AudioCacheStoreCleanupResult(1024, chapterCount, 0, 0)
        };
        var readModel = new CacheReadModelTestDouble
        {
            Books =
            [
                new CachedBookSummary(
                    "book-1",
                    "第一本",
                    "作者甲",
                    chapterCount,
                    chapterCount,
                    chapterCount * 1024L)
            ]
        };
        readModel.ChaptersByBook["book-1"] = chapters.Select(chapter =>
            new CacheChapterView(chapter.ChapterIndex, chapter, statuses[chapter.ChapterIndex])).ToArray();
        return new CacheFixture(
            store,
            readModel,
            new RecordingFeedback());
    }

    private static CacheManagementViewModel CreateViewModel(CacheFixture fixture, IUiScheduler? scheduler = null) =>
        new(
            fixture.Store,
            fixture.ReadModel,
            fixture.Feedback,
            new ConfirmingDialog(),
            new TestNavigator(),
            new TestExportCoordinator(),
            new NoopFileDialogs(),
            scheduler ?? new InlineUiScheduler());

    private sealed record CacheFixture(
        CacheStoreTestDouble Store,
        CacheReadModelTestDouble ReadModel,
        RecordingFeedback Feedback);

    private sealed class RecordingFeedback : IAppFeedbackService
    {
        public string? LastTitle { get; private set; }

        public ProjectedUiError Project(Exception exception) =>
            new(exception.Message, UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected) => LastTitle = title;

        public void ShowSuccess(string title, string message) => LastTitle = title;

        public void ShowWarning(string title, string message) => LastTitle = title;

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(
            string title,
            string message,
            CancellationToken cancellationToken) =>
            Task.FromResult(AppConfirmationDecision.Confirm);
    }

    private sealed class ConfirmingDialog : IAppDialogService
    {
        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken) =>
            Task.FromResult(AppConfirmationDecision.Confirm);

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(
            string title,
            string message,
            string saveButtonText,
            string discardButtonText,
            string cancelButtonText,
            CancellationToken cancellationToken) =>
            Task.FromResult(UnsavedChangesDecision.Cancel);
    }

    private sealed class TestNavigator : IAppNavigator
    {
        public AppRoute CurrentRoute => AppRoutes.CacheManagement;

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(true);

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(true);
    }

    private sealed class TestExportCoordinator : IChapterExportCoordinator
    {
        private EventHandler<ChapterExportSnapshot>? _snapshotChanged;

        public ChapterExportSnapshot? CurrentSnapshot => null;

        public event EventHandler<ChapterExportSnapshot>? SnapshotChanged
        {
            add => _snapshotChanged += value;
            remove => _snapshotChanged -= value;
        }

        public Task<ChapterExportStartResult> StartAsync(
            StartChapterExportRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ChapterExportStartResult(
                ChapterExportStartStatus.Accepted,
                Guid.NewGuid(),
                null));

        public Task CancelAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WaitForCurrentBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopFileDialogs : IPresentationFileDialogService
    {
        public Task<string?> PickOpenFileAsync(
            PresentationFileDialogOptions options,
            CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(
            PresentationFileDialogOptions options,
            CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(
            PresentationFolderDialogOptions options,
            CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class PausingBookList(IReadOnlyList<CachedBookSummary> books) : IReadOnlyList<CachedBookSummary>
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count => books.Count;
        public CachedBookSummary this[int index] => books[index];

        public IEnumerator<CachedBookSummary> GetEnumerator()
        {
            Entered.TrySetResult();
            // The global query hands off this snapshot without enumerating it on the UI.
            // Only the background projection crosses this controlled enumeration barrier.
            Release.Task.GetAwaiter().GetResult();
            return books.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class PausingUiScheduler : IUiScheduler
    {
        public bool PauseNextLater { get; set; }
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        public async Task InvokeLaterAsync(Action action, CancellationToken cancellationToken = default)
        {
            if (PauseNextLater)
            {
                PauseNextLater = false;
                Paused.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            action();
        }
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
