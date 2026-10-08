using NovelSpeaker.TestKit.Cache;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.App.Shared.Presentation.Cache;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell.Activation;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Shared;

public sealed class ChapterCacheViewQuerySlotTests
{
    [Fact]
    public async Task Failure_with_pending_requests_keeps_the_retry_attached_to_activation_until_completion()
    {
        using var owner = new PageActivationController();
        var activation = owner.Activate();
        // Complete the first query on a worker with no synchronization context so
        // its observer has finished before inspecting the outstanding retry.
        var first = new TaskCompletionSource<IReadOnlyList<ChapterCacheStatus>>();
        var retry = new TaskCompletionSource<IReadOnlyList<ChapterCacheStatus>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CacheReadModelTestDouble
        {
            CoverageHandler = (_, indices, _) => indices.Contains(0) ? first.Task : retry.Task
        };
        var applied = new List<int>();
        var failures = 0;
        var slot = new ChapterCacheViewQuerySlot(service, new ImmediateUiScheduler(),
            (_, indices, _) => applied.AddRange(indices), _ => failures++);
        slot.Activate(activation.CancellationToken, activation);
        activation.Register(slot.Deactivate);
        await Task.Run(() => slot.Request("book-1", [0]));
        slot.Request("book-1", [1]);
        await Task.Run(() => first.SetException(new IOException("first query failed")));

        var drain = activation.WaitForPendingOperationsAsync();
        Assert.False(drain.IsCompleted);
        Assert.Equal(1, failures);
        owner.Deactivate();
        retry.SetResult([new(1, 1, 1)]);
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(applied);
        Assert.Equal(1, failures);
    }

    [Fact]
    public async Task Requests_arriving_during_a_refresh_are_coalesced_into_one_follow_up_query()
    {
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothResultsApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestedBatches = new List<int[]>();
        var service = new CacheReadModelTestDouble
        {
            CoverageHandler = async (_, chapterIndices, cancellationToken) =>
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
        var controller = new ChapterCacheViewQuerySlot(
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
    public void New_catalog_activation_rejects_the_previous_result_even_when_book_identity_matches()
    {
        var scheduler = new QueuedUiScheduler();
        var service = new CacheReadModelTestDouble { Statuses = [new(0, 1, 4)] };
        var applied = new List<CacheChapterView>();
        var controller = new ChapterCacheViewQuerySlot(service, scheduler,
            (_, _, views) => applied.AddRange(views), _ => { });
        controller.Activate(CancellationToken.None);
        controller.Request("book-1", [0]);

        controller.Activate(CancellationToken.None);
        service.Statuses = [new(0, 4, 4)];
        controller.Request("book-1", [0]);
        scheduler.RunNext();
        Assert.Empty(applied);
        scheduler.RunNext();
        Assert.Equal(4, Assert.Single(applied).Coverage.CachedSegmentCount);
        controller.Deactivate();
    }

    [Fact]
    public void Deactivate_discards_a_result_waiting_to_reach_the_ui()
    {
        var scheduler = new QueuedUiScheduler();
        var service = new CacheReadModelTestDouble
        {
            CoverageHandler = (_, chapterIndices, _) =>
                Task.FromResult<IReadOnlyList<ChapterCacheStatus>>(
                    chapterIndices.Select(static index => new ChapterCacheStatus(index, 1, 1)).ToArray())
        };
        var applyCount = 0;
        var controller = new ChapterCacheViewQuerySlot(
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
