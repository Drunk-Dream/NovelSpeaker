using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Cache;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class CacheReadModelTests
{
    [Fact]
    public async Task Change_revision_excludes_mutations_committed_after_its_batch_was_dequeued()
    {
        await using var invalidation = new CacheInvalidationCoordinator(new ManualTimeProvider());
        var publishNext = true;
        invalidation.BatchPublished += (_, _) =>
        {
            if (publishNext)
            {
                publishNext = false;
                invalidation.Publish(CacheInvalidation.ForBook("book-B", CacheInvalidationAspect.Coverage));
            }
        };
        using var model = new CacheReadModel(new CacheCatalogTestDouble(), new CacheCoverageTestDouble(),
            new Metadata(), new PendingRepair(), invalidation);
        var changes = new List<CacheReadModelChange>();
        model.Changed += (_, change) => changes.Add(change);
        invalidation.Publish(CacheInvalidation.ForBook("book-A", CacheInvalidationAspect.Coverage));
        await invalidation.FlushPendingAsync(CancellationToken.None);
        await invalidation.FlushPendingAsync(CancellationToken.None);
        Assert.Equal([1L, 2L], changes.Select(change => change.Revision));
        Assert.Equal(["book-A", "book-B"], changes.Select(change =>
            Assert.IsType<CacheReadModelScope.Book>(Assert.Single(change.Scopes)).BookId));
        Assert.All(changes, change => Assert.False(change.OverviewChanged));
    }

    [Fact]
    public async Task Repeated_missing_plan_queries_share_background_repair_and_completion_changes_only_that_chapter()
    {
        await using var invalidation = new CacheInvalidationCoordinator(new ManualTimeProvider());
        var content = new GatedContent();
        await using var repair = new SpeechPlanRepairCoordinator(content, new Settings(), invalidationCoordinator: invalidation);
        var coverage = new CacheCoverageTestDouble
        {
            Statuses = [new(2, 0, null) { Kind = ChapterCacheStatusKind.PlanMissing }]
        };
        using var model = new CacheReadModel(new CacheCatalogTestDouble(), coverage, new Metadata(), repair, invalidation);
        var changes = new List<CacheReadModelChange>();
        model.Changed += (_, change) => changes.Add(change);
        await model.GetChaptersAsync("book", [2], CancellationToken.None);
        await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await model.GetChaptersAsync("book", [2], CancellationToken.None);
        Assert.Equal(1, content.LoadCount);
        Assert.Empty(changes);
        var completion = repair.RequestAsync(new SpeechPlanRepairRequest("book", 2, "chapter-2"), CancellationToken.None);
        content.Release.SetResult();
        await completion.WaitAsync(TimeSpan.FromSeconds(10));
        await invalidation.FlushPendingAsync(CancellationToken.None);
        var scope = Assert.IsType<CacheReadModelScope.Chapters>(Assert.Single(Assert.Single(changes).Scopes));
        Assert.Equal("book", scope.BookId);
        Assert.Equal([2], scope.ChapterIndices);
        await repair.StopAsync(CancellationToken.None);
        await repair.RequestAsync(new SpeechPlanRepairRequest("other", 1, "other-chapter"), CancellationToken.None);
        Assert.Equal(1, content.LoadCount);
    }

    [Fact]
    public async Task Sparse_view_preserves_physical_cache_and_current_configuration_states_without_waiting_for_repair()
    {
        await using var invalidation = new CacheInvalidationCoordinator(new ManualTimeProvider());
        var catalog = new CacheCatalogTestDouble();
        catalog.ChaptersByBook["book"] = Enumerable.Range(0, 6)
            .Select(index => new CachedChapterSummary("book", index, "Chapter", 2, 2, 100)).ToArray();
        var coverage = new CacheCoverageTestDouble
        {
            Statuses = [
                new(0, 2, 2), new(1, 0, 2),
                new(2, 0, null) { Kind = ChapterCacheStatusKind.PlanMissing },
                new(3, 0, null) { Kind = ChapterCacheStatusKind.PlanStale },
                new(4, 0, null), new(5, 0, 0)]
        };
        var repair = new PendingRepair();
        using var model = new CacheReadModel(catalog, coverage, new Metadata(), repair, invalidation);

        var result = await model.GetChaptersAsync("book", [0, 1, 2, 3, 4, 5, 6], CancellationToken.None);

        Assert.Equal(7, result.Value.Count);
        Assert.All(result.Value.Take(6), chapter => Assert.Equal(100, chapter.Physical!.TotalSizeBytes));
        Assert.True(result.Value[0].IsExportable);
        Assert.All(result.Value.Skip(1), chapter => Assert.False(chapter.IsExportable));
        Assert.Equal(0, result.Value[1].Coverage.CachedSegmentCount);
        Assert.Equal(ChapterCacheStatusKind.PlanMissing, result.Value[2].Coverage.Kind);
        Assert.Equal(ChapterCacheStatusKind.PlanStale, result.Value[3].Coverage.Kind);
        Assert.Equal(ChapterCacheStatusKind.ConfigurationUnavailable, result.Value[4].Coverage.Kind);
        Assert.Null(result.Value[6].Physical);
        Assert.Equal([2, 3], repair.Requests.Select(request => request.ChapterIndex));
        Assert.False(repair.Completion.Task.IsCompleted);
        repair.Completion.SetResult();
    }

    [Fact]
    public async Task Mutation_during_composition_is_retried_inside_cache_even_before_coalesced_notification()
    {
        await using var invalidation = new CacheInvalidationCoordinator(new ManualTimeProvider());
        var catalog = new CacheCatalogTestDouble();
        catalog.ChaptersByBook["book"] = [new("book", 0, "Chapter", 1, 1, 100)];
        var coverage = new CacheCoverageTestDouble();
        var changed = false;
        coverage.CoverageHandler = (_, _, _) =>
        {
            if (!changed)
            {
                changed = true;
                catalog.ChaptersByBook["book"] = [new("book", 0, "Chapter", 2, 2, 200)];
                invalidation.Publish(CacheInvalidation.ForChapters("book", [0], CacheInvalidationAspect.PhysicalSummary));
            }

            return Task.FromResult<IReadOnlyList<ChapterCacheStatus>>([new(0, 2, 2)]);
        };
        using var model = new CacheReadModel(catalog, coverage, new Metadata(), new PendingRepair(), invalidation);
        var notifications = new List<CacheReadModelChange>();
        model.Changed += (_, change) => notifications.Add(change);
        var result = await model.GetChaptersAsync("book", [0], CancellationToken.None);
        Assert.Equal(200, Assert.Single(result.Value).Physical!.TotalSizeBytes);
        Assert.Equal(model.Revision, result.Revision);
        Assert.Empty(notifications);
        await invalidation.FlushPendingAsync(CancellationToken.None);
        Assert.Single(notifications);
    }

    [Fact]
    public async Task Committed_changes_coalesce_preserve_scope_isolate_observers_and_drain_at_shutdown()
    {
        await using var invalidation = new CacheInvalidationCoordinator(new ManualTimeProvider());
        using var model = new CacheReadModel(new CacheCatalogTestDouble(), new CacheCoverageTestDouble(),
            new Metadata(), new PendingRepair(), invalidation);
        var notifications = new List<CacheReadModelChange>();
        model.Changed += (_, _) => throw new InvalidOperationException("observer");
        model.Changed += (_, change) => notifications.Add(change);
        Assert.Empty(notifications);
        invalidation.Publish(CacheInvalidation.ForChapters("book", [3, 3], CacheInvalidationAspect.Coverage));
        invalidation.Publish(CacheInvalidation.ForChapters("book", [1], CacheInvalidationAspect.PhysicalSummary));
        Assert.Empty(notifications);
        await invalidation.StopAsync(CancellationToken.None);
        var change = Assert.Single(notifications);
        var scope = Assert.IsType<CacheReadModelScope.Chapters>(Assert.Single(change.Scopes));
        Assert.Equal("book", scope.BookId);
        Assert.Equal([1, 3], scope.ChapterIndices);
        Assert.Equal(model.Revision, change.Revision);
        Assert.True(change.OverviewChanged);
    }

    private sealed class Metadata : IBookPlaybackMetadataQuery
    {
        public Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackBookMetadata?>(null);

        public Task<PlaybackChapterMetadata?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackChapterMetadata?>(new(bookId, chapterIndex, "Chapter", "source", $"chapter-{chapterIndex}"));
    }

    private sealed class PendingRepair : ISpeechPlanRepairCoordinator
    {
        public List<SpeechPlanRepairRequest> Requests { get; } = [];
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RequestAsync(SpeechPlanRepairRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Completion.Task;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Settings : IAppSettingsService
    {
        public AppSettings Current => AppSettings.Default;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }
        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class GatedContent : IBookPlaybackContentService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int LoadCount { get; private set; }
        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            LoadCount++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return PlaybackChapterContent.FromLoaded(chapterIndex, "Chapter", [], $"chapter-{chapterIndex}");
        }
    }
}
