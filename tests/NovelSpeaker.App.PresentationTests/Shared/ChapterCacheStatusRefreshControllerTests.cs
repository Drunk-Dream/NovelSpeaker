using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.App.Shared.Presentation.Cache;
using NovelSpeaker.App.Shared.Presentation.Platform;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Shared;

public sealed class ChapterCacheStatusRefreshControllerTests
{
    [Fact]
    public async Task Initial_projection_flag_is_preserved_for_the_initial_batch_only()
    {
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothResultsApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var projectionModes = new List<bool>();
        var service = new FakeCacheWorkspaceService
        {
            StatusHandler = async (_, chapterIndices, cancellationToken) =>
            {
                if (projectionModes.Count == 0)
                {
                    firstRequestStarted.TrySetResult();
                    await releaseFirstRequest.Task.WaitAsync(cancellationToken);
                }

                return chapterIndices
                    .Select(static chapterIndex => new ChapterCacheStatus(chapterIndex, 1, 1))
                    .ToArray();
            }
        };
        var controller = new ChapterCacheStatusRefreshController(
            service,
            new ImmediateUiScheduler(),
            (_, _, _, isInitialProjection) =>
            {
                projectionModes.Add(isInitialProjection);
                if (projectionModes.Count == 2)
                {
                    bothResultsApplied.TrySetResult();
                }
            },
            _ => { });
        controller.Activate(CancellationToken.None);

        controller.Request("book-1", [0], isInitialProjection: true);
        await firstRequestStarted.Task;
        controller.Request("book-1", [1]);
        releaseFirstRequest.TrySetResult();
        await bothResultsApplied.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([true, false], projectionModes);
    }

    [Fact]
    public async Task Requests_arriving_during_a_refresh_are_coalesced_into_one_follow_up_query()
    {
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothResultsApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestedBatches = new List<int[]>();
        var service = new FakeCacheWorkspaceService
        {
            StatusHandler = async (_, chapterIndices, cancellationToken) =>
            {
                requestedBatches.Add([.. chapterIndices.Order()]);
                if (requestedBatches.Count == 1)
                {
                    firstRequestStarted.TrySetResult();
                    await releaseFirstRequest.Task.WaitAsync(cancellationToken);
                }

                return chapterIndices
                    .Select(static chapterIndex => new ChapterCacheStatus(chapterIndex, 1, 1))
                    .ToArray();
            }
        };
        var applyCount = 0;
        var controller = new ChapterCacheStatusRefreshController(
            service,
            new ImmediateUiScheduler(),
            (_, _, _, _) =>
            {
                if (Interlocked.Increment(ref applyCount) == 2)
                {
                    bothResultsApplied.TrySetResult();
                }
            },
            _ => { });
        controller.Activate(CancellationToken.None);

        controller.Request("book-1", [0]);
        await firstRequestStarted.Task;
        controller.Request("book-1", [1]);
        controller.Request("book-1", [1, 2]);
        releaseFirstRequest.TrySetResult();
        await bothResultsApplied.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, requestedBatches.Count);
        Assert.Equal([0], requestedBatches[0]);
        Assert.Equal([1, 2], requestedBatches[1]);
    }

    [Fact]
    public void Deactivate_discards_a_result_waiting_to_reach_the_ui()
    {
        var scheduler = new QueuedUiScheduler();
        var service = new FakeCacheWorkspaceService
        {
            StatusHandler = (_, chapterIndices, _) =>
                Task.FromResult<IReadOnlyList<ChapterCacheStatus>>(
                    chapterIndices.Select(static index => new ChapterCacheStatus(index, 1, 1)).ToArray())
        };
        var applyCount = 0;
        var controller = new ChapterCacheStatusRefreshController(
            service,
            scheduler,
            (_, _, _, _) => applyCount++,
            _ => { });
        controller.Activate(CancellationToken.None);

        controller.Request("book-1", [0]);
        Assert.Equal(1, scheduler.PendingCount);

        controller.Deactivate();
        scheduler.RunNext();

        Assert.Equal(0, applyCount);
    }

    private sealed class FakeCacheWorkspaceService : ICacheWorkspaceService
    {
        public Func<string, IReadOnlyCollection<int>, CancellationToken, Task<IReadOnlyList<ChapterCacheStatus>>> StatusHandler { get; init; } =
            (_, _, _) => Task.FromResult<IReadOnlyList<ChapterCacheStatus>>([]);

        public event EventHandler<CacheChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ChapterCacheStatus>> GetChapterCacheStatusesAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            StatusHandler(bookId, chapterIndices, cancellationToken);

        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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

        public int PendingCount => _pending.Count;

        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue((action, completion));
            return completion.Task;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void RunNext()
        {
            var pending = _pending.Dequeue();
            pending.Action();
            pending.Completion.TrySetResult();
        }
    }
}
