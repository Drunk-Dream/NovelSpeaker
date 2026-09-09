using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class CacheManagementViewModelTests
{
    [Fact]
    public async Task Loading_and_selecting_book_projects_catalog_and_current_coverage()
    {
        var cache = CreateCache();
        var viewModel = CreateViewModel(cache);

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
        var cache = CreateCache();
        var viewModel = CreateViewModel(cache);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectBookCommand.ExecuteAsync(viewModel.Books[0]);
        viewModel.HandleChapterClick(viewModel.Chapters[0], DesktopSelectionModifiers.None);

        await viewModel.ClearSelectedChaptersCommand.ExecuteAsync(null);

        Assert.Equal(("book-1", new[] { 0 }), cache.LastClearChaptersRequest);
        Assert.Equal(1, cache.ClearChaptersCallCount);
        Assert.Equal("缓存已清理", cache.Feedback.LastTitle);
    }

    [Fact]
    public async Task Large_catalog_is_replaced_as_one_collection_projection()
    {
        var cache = CreateCache(chapterCount: 10_000);
        var viewModel = CreateViewModel(cache);
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

    private static CacheTestDouble CreateCache(int chapterCount = 2)
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
        return new CacheTestDouble(chapters, statuses);
    }

    private static CacheManagementViewModel CreateViewModel(CacheTestDouble cache) =>
        new(
            cache,
            cache,
            cache,
            cache,
            cache.Feedback,
            new ConfirmingDialog(),
            new TestNavigator(),
            new TestExportCoordinator(),
            new NoopFileDialogs(),
            new InlineUiScheduler());

    private sealed class CacheTestDouble : IAudioCacheStore, ICacheCatalog, ICacheCoverageQuery, ICacheInvalidationCoordinator
    {
        private readonly IReadOnlyList<CachedChapterSummary> _chapters;
        private readonly IReadOnlyList<ChapterCacheStatus> _statuses;
        private EventHandler<CacheInvalidationBatch>? _batchPublished;

        public CacheTestDouble(
            IReadOnlyList<CachedChapterSummary> chapters,
            IReadOnlyList<ChapterCacheStatus> statuses)
        {
            _chapters = chapters;
            _statuses = statuses;
            Feedback = new RecordingFeedback();
        }

        public RecordingFeedback Feedback { get; }

        public int ClearChaptersCallCount { get; private set; }

        public (string BookId, int[] ChapterIndices)? LastClearChaptersRequest { get; private set; }

        public event EventHandler<CacheInvalidationBatch>? BatchPublished
        {
            add => _batchPublished += value;
            remove => _batchPublished -= value;
        }

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CacheOverviewModel(1024, _chapters.Count, AppSettings.DefaultCacheLimitBytes, false));

        public Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CachedBookSummary>>(
            [new CachedBookSummary("book-1", "第一本", "作者甲", _chapters.Count, _chapters.Count, _chapters.Count * 1024L)]);

        public Task<IReadOnlyList<CachedBookSummary>> GetCachedBooksAsync(
            IReadOnlyCollection<string> bookIds,
            CancellationToken cancellationToken) =>
            GetCachedBooksAsync(cancellationToken);

        public Task<CachedBookSummary?> GetCachedBookAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CachedBookSummary?>(
                new CachedBookSummary("book-1", "第一本", "作者甲", _chapters.Count, _chapters.Count, _chapters.Count * 1024L));

        public Task<IReadOnlyList<CachedChapterCatalogEntry>> GetCachedChapterCatalogAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CachedChapterCatalogEntry>>(
                _chapters.Select(chapter => new CachedChapterCatalogEntry(
                    chapter.BookId,
                    chapter.ChapterIndex,
                    chapter.Title)).ToArray());

        public Task<IReadOnlyList<CachedChapterSummary>> GetCachedChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CachedChapterSummary>>(
                _chapters.Where(chapter => chapterIndices.Contains(chapter.ChapterIndex)).ToArray());

        public Task<CachedChapterSummary?> GetCachedChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            Task.FromResult(_chapters.FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex));

        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChapterCacheStatus>>(
                _statuses.Where(status => chapterIndices.Contains(status.ChapterIndex)).ToArray());

        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            IReadOnlyCollection<PlaybackChapterMetadata> chapters,
            CancellationToken cancellationToken) =>
            GetAsync(bookId, chapterIndices, cancellationToken);

        public Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AudioCacheStoreSummary(1024, _chapters.Count, AppSettings.DefaultCacheLimitBytes, false));

        public Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CachedBookStoreSummary>>([]);

        public Task<CachedBookStoreSummary?> GetBookAsync(string bookId, CancellationToken cancellationToken) =>
            Task.FromResult<CachedBookStoreSummary?>(null);

        public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CachedChapterStoreSummary>>(
                _chapters.Select(chapter => new CachedChapterStoreSummary(
                    chapter.BookId,
                    chapter.ChapterIndex,
                    chapter.DistinctSegmentCount,
                    chapter.EntryCount,
                    chapter.TotalSizeBytes)).ToArray());

        public Task<CachedChapterStoreSummary?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            Task.FromResult<CachedChapterStoreSummary?>(null);

        public Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(
            IReadOnlyCollection<CurrentCacheChapterQuery> chapters,
            SynthesisProfileFingerprint synthesisProfile,
            CancellationToken cancellationToken) =>
            Task.FromResult(_statuses);

        public Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesAsync(
            IReadOnlyCollection<AudioCacheKey> keys,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<AudioCacheKey>>(new HashSet<AudioCacheKey>());

        public Task<AudioCacheStoreCleanupResult> ClearChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AudioCacheStoreCleanupResult(1024, 1, 0, 0));

        public Task<AudioCacheStoreCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken)
        {
            ClearChaptersCallCount++;
            LastClearChaptersRequest = (bookId, chapterIndices.ToArray());
            Publish(CacheInvalidation.ForChapters(
                bookId,
                chapterIndices,
                CacheInvalidationAspect.PhysicalSummary |
                CacheInvalidationAspect.CatalogStructure |
                CacheInvalidationAspect.Coverage));
            return Task.FromResult(new AudioCacheStoreCleanupResult(1024, chapterIndices.Count, 0, 0));
        }

        public Task<AudioCacheStoreCleanupResult> ClearBookAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AudioCacheStoreCleanupResult(1024, 1, 0, 0));

        public Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AudioCacheStoreCleanupResult(1024, 1, 0, 0));

        public Task RunMaintenanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RunStartupMaintenanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RequestAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            IReadOnlyCollection<ChapterCacheStatus> statuses,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void Publish(CacheInvalidation invalidation) =>
            _batchPublished?.Invoke(this, new CacheInvalidationBatch([invalidation]));

        public Task FlushPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

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
