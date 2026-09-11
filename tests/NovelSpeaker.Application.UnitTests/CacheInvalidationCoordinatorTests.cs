using NovelSpeaker.Application.Cache;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class CacheInvalidationCoordinatorTests
{
    [Fact]
    public async Task FlushPendingAsync_merges_and_deduplicates_chapter_mutations()
    {
        var timeProvider = new ManualTimeProvider();
        await using var coordinator = new CacheInvalidationCoordinator(timeProvider);
        var batches = new List<CacheInvalidationBatch>();
        coordinator.BatchPublished += (_, batch) => batches.Add(batch);

        coordinator.Publish(CacheInvalidation.ForChapters(
            "book-1",
            [5, 1, 5],
            CacheInvalidationAspect.PhysicalSummary));
        coordinator.Publish(CacheInvalidation.ForChapters(
            "book-1",
            [3, 1],
            CacheInvalidationAspect.CatalogStructure));

        await coordinator.FlushPendingAsync(CancellationToken.None);

        var change = Assert.Single(Assert.Single(batches).Changes);
        var scope = Assert.IsType<CacheInvalidationScope.Chapters>(change.Scope);
        Assert.Equal([1, 3, 5], scope.ChapterIndices);
        Assert.Equal(
            CacheInvalidationAspect.PhysicalSummary | CacheInvalidationAspect.CatalogStructure,
            change.Aspects);
    }

    [Fact]
    public async Task Global_invalidation_covers_narrower_changes_of_the_same_aspects()
    {
        var timeProvider = new ManualTimeProvider();
        await using var coordinator = new CacheInvalidationCoordinator(timeProvider);
        CacheInvalidationBatch? published = null;
        coordinator.BatchPublished += (_, batch) => published = batch;

        coordinator.Publish(CacheInvalidation.ForChapters(
            "book-1",
            [1],
            CacheInvalidationAspect.PhysicalSummary | CacheInvalidationAspect.CatalogStructure));
        coordinator.Publish(CacheInvalidation.ForBook(
            "book-1",
            CacheInvalidationAspect.PhysicalSummary));
        coordinator.Publish(CacheInvalidation.ForGlobal(
            CacheInvalidationAspect.PhysicalSummary | CacheInvalidationAspect.CatalogStructure));

        await coordinator.FlushPendingAsync(CancellationToken.None);

        var batch = published ?? throw new Xunit.Sdk.XunitException("No invalidation batch was published.");
        var change = Assert.Single(batch.Changes);
        Assert.IsType<CacheInvalidationScope.Global>(change.Scope);
        Assert.Equal(
            CacheInvalidationAspect.PhysicalSummary | CacheInvalidationAspect.CatalogStructure,
            change.Aspects);
    }

    [Fact]
    public async Task Global_invalidation_removes_only_the_covered_aspects_from_narrower_changes()
    {
        var timeProvider = new ManualTimeProvider();
        await using var coordinator = new CacheInvalidationCoordinator(timeProvider);
        CacheInvalidationBatch? published = null;
        coordinator.BatchPublished += (_, batch) => published = batch;

        coordinator.Publish(CacheInvalidation.ForChapters(
            "book-1",
            [1],
            CacheInvalidationAspect.PhysicalSummary | CacheInvalidationAspect.Coverage));
        coordinator.Publish(CacheInvalidation.ForGlobal(CacheInvalidationAspect.Coverage));

        await coordinator.FlushPendingAsync(CancellationToken.None);

        var batch = published ?? throw new Xunit.Sdk.XunitException("No invalidation batch was published.");
        Assert.Equal(2, batch.Changes.Count);
        Assert.Contains(batch.Changes, change => change.Scope is CacheInvalidationScope.Global &&
                                                 change.Aspects == CacheInvalidationAspect.Coverage);
        var chapterChange = Assert.Single(
            batch.Changes,
            change => change.Scope is CacheInvalidationScope.Chapters);
        Assert.Equal(CacheInvalidationAspect.PhysicalSummary, chapterChange.Aspects);
    }

    [Fact]
    public async Task Book_invalidation_covers_narrower_chapter_changes_in_both_orders()
    {
        var timeProvider = new ManualTimeProvider();
        await using var coordinator = new CacheInvalidationCoordinator(timeProvider);
        var batches = new List<CacheInvalidationBatch>();
        coordinator.BatchPublished += (_, batch) => batches.Add(batch);

        coordinator.Publish(CacheInvalidation.ForChapters(
            "book-1",
            [1],
            CacheInvalidationAspect.PhysicalSummary));
        coordinator.Publish(CacheInvalidation.ForBook(
            "book-1",
            CacheInvalidationAspect.PhysicalSummary));

        await coordinator.FlushPendingAsync(CancellationToken.None);

        var firstChange = Assert.Single(Assert.Single(batches).Changes);
        Assert.IsType<CacheInvalidationScope.Book>(firstChange.Scope);
        Assert.Equal(CacheInvalidationAspect.PhysicalSummary, firstChange.Aspects);

        batches.Clear();
        coordinator.Publish(CacheInvalidation.ForBook(
            "book-1",
            CacheInvalidationAspect.PhysicalSummary));
        coordinator.Publish(CacheInvalidation.ForChapters(
            "book-1",
            [2],
            CacheInvalidationAspect.PhysicalSummary));

        await coordinator.FlushPendingAsync(CancellationToken.None);

        var secondChange = Assert.Single(Assert.Single(batches).Changes);
        Assert.IsType<CacheInvalidationScope.Book>(secondChange.Scope);
        Assert.Equal(CacheInvalidationAspect.PhysicalSummary, secondChange.Aspects);
    }

    [Fact]
    public async Task StopAsync_flushes_pending_changes_and_rejects_later_mutations()
    {
        var timeProvider = new ManualTimeProvider();
        await using var coordinator = new CacheInvalidationCoordinator(timeProvider);
        var batches = new List<CacheInvalidationBatch>();
        coordinator.BatchPublished += (_, batch) => batches.Add(batch);

        coordinator.Publish(CacheInvalidation.ForBook(
            "book-1",
            CacheInvalidationAspect.Coverage));
        await coordinator.StopAsync(CancellationToken.None);
        coordinator.Publish(CacheInvalidation.ForGlobal(CacheInvalidationAspect.Coverage));

        var change = Assert.Single(Assert.Single(batches).Changes);
        Assert.Equal(CacheInvalidationAspect.Coverage, change.Aspects);
        Assert.IsType<CacheInvalidationScope.Book>(change.Scope);
    }
}
