using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.App.Shared.Presentation.Cache;
using NovelSpeaker.App.Shared.Presentation.Platform;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Shared;

public sealed class ChapterCacheStatusRefreshControllerTests
{
    [Fact]
    public async Task Requests_arriving_during_a_refresh_are_coalesced_into_one_follow_up_query()
    {
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothResultsApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestedBatches = new List<int[]>();
        var service = new FakeCacheCoverageQuery
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
        var appliedBatches = new List<int[]>();
        var controller = new ChapterCacheStatusRefreshController(
            service,
            new ImmediateUiScheduler(),
            (_, chapterIndices, _) =>
            {
                appliedBatches.Add([.. chapterIndices.Order()]);
                if (appliedBatches.Count == 2)
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
        Assert.Equal([[0], [1, 2]], appliedBatches);
    }

    [Fact]
    public void Deactivate_discards_a_result_waiting_to_reach_the_ui()
    {
        var scheduler = new QueuedUiScheduler();
        var service = new FakeCacheCoverageQuery
        {
            StatusHandler = (_, chapterIndices, _) =>
                Task.FromResult<IReadOnlyList<ChapterCacheStatus>>(
                    chapterIndices.Select(static index => new ChapterCacheStatus(index, 1, 1)).ToArray())
        };
        var applyCount = 0;
        var controller = new ChapterCacheStatusRefreshController(
            service,
            scheduler,
            (_, _, _) => applyCount++,
            _ => { });
        controller.Activate(CancellationToken.None);

        controller.Request("book-1", [0]);
        Assert.Equal(1, scheduler.PendingCount);

        controller.Deactivate();
        scheduler.RunNext();

        Assert.Equal(0, applyCount);
    }

    private sealed class FakeCacheCoverageQuery : ICacheCoverageQuery
    {
        public Func<string, IReadOnlyCollection<int>, CancellationToken, Task<IReadOnlyList<ChapterCacheStatus>>> StatusHandler { get; init; } =
            (_, _, _) => Task.FromResult<IReadOnlyList<ChapterCacheStatus>>([]);

        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            StatusHandler(bookId, chapterIndices, cancellationToken);

        public Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            IReadOnlyCollection<PlaybackChapterMetadata> chapters,
            CancellationToken cancellationToken) =>
            StatusHandler(bookId, chapterIndices, cancellationToken);
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
