using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
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
    public async Task Browsing_large_missing_plan_catalog_does_not_request_repair_or_load_chapter_content()
    {
        await using var invalidation = new CacheInvalidationCoordinator(new ManualTimeProvider());
        var repair = new PendingRepair();
        var coverage = new CacheCoverageTestDouble();
        coverage.CoverageHandler = (_, indices, _) => Task.FromResult<IReadOnlyList<ChapterCacheStatus>>(
            indices.Select(index => new ChapterCacheStatus(index, 0, null)
            {
                Kind = ChapterCacheStatusKind.PlanMissing
            }).ToArray());
        using var model = new CacheReadModel(new CacheCatalogTestDouble(), coverage, new Metadata(), repair, invalidation);

        for (var start = 0; start < 10_000; start += 80)
        {
            var indices = Enumerable.Range(start, Math.Min(80, 10_000 - start)).ToArray();
            var result = await model.GetChaptersAsync("book", indices, CancellationToken.None);
            Assert.All(result.Value, chapter => Assert.Equal(ChapterCacheStatusKind.PlanMissing, chapter.Coverage.Kind));
        }

        Assert.Equal(125, coverage.CoverageQueryCallCount);
        Assert.Empty(repair.Requests);
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
                new(4, 0, null), new(5, 0, 0),
                new(7, 0, null) { Kind = ChapterCacheStatusKind.PlanStale }]
        };
        var repair = new PendingRepair();
        using var model = new CacheReadModel(catalog, coverage, new Metadata(), repair, invalidation);

        var result = await model.GetChaptersAsync("book", [0, 1, 2, 3, 4, 5, 6, 7], CancellationToken.None);

        Assert.Equal(8, result.Value.Count);
        Assert.All(result.Value.Take(6), chapter => Assert.Equal(100, chapter.Physical!.TotalSizeBytes));
        Assert.True(result.Value[0].IsExportable);
        Assert.All(result.Value.Skip(1), chapter => Assert.False(chapter.IsExportable));
        Assert.Equal(0, result.Value[1].Coverage.CachedSegmentCount);
        Assert.Equal(ChapterCacheStatusKind.PlanMissing, result.Value[2].Coverage.Kind);
        Assert.Equal(ChapterCacheStatusKind.PlanStale, result.Value[3].Coverage.Kind);
        Assert.Equal(ChapterCacheStatusKind.ConfigurationUnavailable, result.Value[4].Coverage.Kind);
        Assert.Null(result.Value[6].Physical);
        Assert.Equal(ChapterCacheStatusKind.PlanStale, result.Value[7].Coverage.Kind);
        Assert.Null(result.Value[7].Physical);
        Assert.Equal([3], repair.Requests.Select(request => request.ChapterIndex));
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

}
