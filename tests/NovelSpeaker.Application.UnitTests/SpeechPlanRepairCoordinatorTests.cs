using System.Collections.Concurrent;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class SpeechPlanRepairCoordinatorTests
{
    [Fact]
    public async Task Audio_removed_before_repair_cleans_only_that_chapter_without_reading_text()
    {
        var fixture = new Fixture { MissingAudioChapter = 0 };
        await using var worker = fixture.CreateWorker();
        using var window = new CancellationTokenSource();
        worker.UpdateDemand([Request(0), Request(1)], window.Token);
        await fixture.Published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal([0], fixture.Cleaned);
        Assert.Equal([1], fixture.Reads);
        Assert.Equal(["chapter-1"], fixture.Saved);
    }

    [Fact]
    public async Task Rapid_window_changes_and_leaving_discard_queued_and_running_old_work()
    {
        var fixture = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Load = async index =>
        {
            if (index == 0) { started.SetResult(); await release.Task; }
        };
        await using var worker = fixture.CreateWorker();
        using var initial = new CancellationTokenSource();
        worker.UpdateDemand([Request(0), Request(1)], initial.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        initial.Cancel();
        for (var index = 2; index < 102; index++)
        {
            using var window = new CancellationTokenSource();
            worker.UpdateDemand([Request(index)], window.Token);
            window.Cancel();
        }
        using var latest = new CancellationTokenSource();
        worker.UpdateDemand([Request(999), Request(999)], latest.Token);
        Assert.Equal([0], fixture.Reads);
        release.SetResult();
        var change = await fixture.Published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal([0, 999], fixture.Reads);
        Assert.Equal(["chapter-999"], fixture.Saved);
        Assert.Equal([999], Assert.IsType<CacheInvalidationScope.Chapters>(change.Scope).ChapterIndices);
    }

    [Fact]
    public async Task Shutdown_honors_the_caller_budget_and_rejects_late_results()
    {
        var fixture = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Load = async _ => { started.SetResult(); await release.Task; };
        await using var worker = fixture.CreateWorker();
        using var window = new CancellationTokenSource();
        worker.UpdateDemand([Request(0)], window.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var budget = new CancellationTokenSource();
        var stop = worker.StopAsync(budget.Token);
        Assert.False(stop.IsCompleted);
        budget.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);
        release.SetResult();
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(fixture.Saved);
        Assert.False(fixture.Published.Task.IsCompleted);
    }

    [Fact]
    public async Task Failed_repairs_are_not_retried_by_refresh_and_worker_continues()
    {
        var fixture = new Fixture();
        fixture.Load = index => index == 0 ? Task.FromException(new IOException("test failure")) : Task.CompletedTask;
        await using var worker = fixture.CreateWorker();
        using var window = new CancellationTokenSource();
        worker.UpdateDemand([Request(0)], window.Token);
        await fixture.Failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var index = 0; index < 100; index++) worker.UpdateDemand([Request(0)], window.Token);
        worker.UpdateDemand([Request(0), Request(1)], window.Token);
        await fixture.Published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal([0, 1], fixture.Reads);
        Assert.Equal(["chapter-1"], fixture.Saved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changed_profile_or_source_rejects_late_computation(bool changeProfile)
    {
        var fixture = new Fixture();
        fixture.Load = index =>
        {
            if (index == 0)
            {
                if (changeProfile) fixture.Current = fixture.Current with { LongParagraphThreshold = 999 };
                else fixture.SourceChanged = true;
            }
            return Task.CompletedTask;
        };
        await using var worker = fixture.CreateWorker();
        using var window = new CancellationTokenSource();
        worker.UpdateDemand([Request(0), Request(1)], window.Token);
        await fixture.Published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(["chapter-1"], fixture.Saved);
    }

    private static SpeechPlanRepairRequest Request(int index) => new("book", index, $"chapter-{index}");

    private sealed class Fixture : IBookPlaybackContentService, IBookPlaybackMetadataQuery, IAudioCacheStore,
        IChapterSpeechPlanStore, IAppSettingsService, IRegexReplacementRuleRepository, ICacheInvalidationCoordinator,
        ICacheCompletenessFailureReporter
    {
        public Task<ChapterContentReader> FreezeChapterReaderAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ChapterContentReader>(GetChapterAsync);

        public ConcurrentQueue<int> Reads { get; } = new();
        public ConcurrentQueue<string> Saved { get; } = new();
        public ConcurrentQueue<int> Cleaned { get; } = new();
        public int? MissingAudioChapter { get; set; }
        public TaskCompletionSource<CacheInvalidation> Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<int, Task> Load { get; set; } = _ => Task.CompletedTask;
        public bool SourceChanged { get; set; }
        public AppSettings Current { get; set; } = AppSettings.Default;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }
        public event EventHandler<CacheInvalidationBatch>? BatchPublished { add { } remove { } }
        public long Revision => 0;
        public SpeechPlanRepairCoordinator CreateWorker() => new(this, this, this, this, this, this, this, this);
        public void Publish(CacheInvalidation invalidation) => Published.TrySetResult(invalidation);
        public void ReportCompletenessUnavailable(Exception exception) => Failed.TrySetResult();
        public Task FlushPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<PlaybackBookContent?> IBookPlaybackContentService.GetBookAsync(string bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<PlaybackBookMetadata?> IBookPlaybackMetadataQuery.GetBookAsync(string bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<PlaybackChapterMetadata?> IBookPlaybackMetadataQuery.GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackChapterMetadata?>(new(bookId, chapterIndex, "Chapter", "source",
                SourceChanged && chapterIndex == 0 ? "replacement" : $"chapter-{chapterIndex}"));
        public async Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            Reads.Enqueue(chapterIndex);
            var profile = TextProfileFingerprint.Create(Current.ToTextSegmentationOptions(), []);
            await Load(chapterIndex);
            return PlaybackChapterContent.FromLoaded(chapterIndex, "Chapter", [], $"chapter-{chapterIndex}") with
            {
                SpeechPlan = new($"chapter-{chapterIndex}", Fingerprint.Sha256("revision"), profile,
                    Fingerprint.Sha256("output"), ChapterSpeechPlanState.Ready, 0, DateTimeOffset.UtcNow, [])
            };
        }
        Task<CachedChapterStoreSummary?> IAudioCacheStore.GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) =>
            Task.FromResult<CachedChapterStoreSummary?>(chapterIndex == MissingAudioChapter ? null : new(bookId, chapterIndex, 1, 1, 100));
        public Task DeleteIfUncachedAsync(string chapterId, CancellationToken cancellationToken)
        {
            Cleaned.Enqueue(int.Parse(chapterId["chapter-".Length..], System.Globalization.CultureInfo.InvariantCulture));
            return Task.CompletedTask;
        }

        public Task<bool> SaveIfCachedAsync(ChapterSpeechPlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Saved.Enqueue(plan.ChapterId);
            return Task.FromResult(true);
        }
        public Task<ChapterSpeechPlan?> GetAsync(string chapterId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(ChapterSpeechPlan plan, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeletePlansWithoutCacheEntriesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RegexReplacementRule>>([]);
        public Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveOrderAsync(IReadOnlyList<(Guid RuleId, int SortOrder)> order, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CachedBookStoreSummary?> GetBookAsync(string bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(string bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(IReadOnlyCollection<CurrentCacheChapterQuery> chapters, SynthesisProfileFingerprint synthesisProfile, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesAsync(IReadOnlyCollection<AudioCacheKey> keys, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AudioCacheStoreCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AudioCacheStoreCleanupResult> ClearChaptersAsync(string bookId, IReadOnlyCollection<int> chapterIndices, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AudioCacheStoreCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RunMaintenanceAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RunStartupMaintenanceAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
