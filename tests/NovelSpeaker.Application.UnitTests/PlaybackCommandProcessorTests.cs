using System.Collections.Concurrent;
using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackCommandProcessorTests
{
    [Fact]
    public async Task RunSerializedAsync_executes_commands_one_at_a_time_in_submission_order()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = new ConcurrentQueue<int>();
        await using var processor = new PlaybackCommandProcessor(
            (_, _) => Task.CompletedTask,
            static () => { });

        var first = processor.RunSerializedAsync(
            async _ =>
            {
                executions.Enqueue(1);
                firstEntered.TrySetResult();
                await releaseFirst.Task;
            },
            CancellationToken.None);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = processor.RunSerializedAsync(
            _ =>
            {
                executions.Enqueue(2);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(second.IsCompleted);
        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal([1, 2], executions);
    }

    [Fact]
    public async Task Enqueue_deduplicates_identical_events_before_processing()
    {
        var firstHandlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handledCount = 0;
        await using var processor = new PlaybackCommandProcessor(
            (_, _) =>
            {
                var invocation = Interlocked.Increment(ref handledCount);
                if (invocation == 1)
                {
                    firstHandlerEntered.TrySetResult();
                    return releaseFirstHandler.Task;
                }

                return Task.CompletedTask;
            },
            static () => { });
        var command = new PlaybackEventCommand(
            PlaybackEventCommandKind.SnapshotChanged,
            Guid.NewGuid(),
            new LocalAudioPlaybackSnapshot(
                PlaybackState.Playing,
                "测试",
                "book-1",
                0,
                0,
                0,
                1000,
                null,
                false),
            null,
            processor.CurrentEventEpoch);

        processor.Enqueue(command);
        await firstHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        processor.Enqueue(command);
        releaseFirstHandler.TrySetResult();
        await processor.WaitForIdleAsync();

        Assert.Equal(1, handledCount);
    }

    [Fact]
    public async Task BeginShutdown_cancels_the_lifecycle_token_and_rejects_new_commands()
    {
        await using var processor = new PlaybackCommandProcessor(
            (_, _) => Task.CompletedTask,
            static () => { });

        processor.BeginShutdown();

        Assert.True(processor.LifecycleToken.IsCancellationRequested);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            processor.RunSerializedAsync(_ => Task.CompletedTask, CancellationToken.None));
    }
}
