using NovelSpeaker.Application.Playback;
using NovelSpeaker.UnitTests.Speech;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed class PlaybackPrefetchControllerTests
{
    [Fact]
    public async Task SubmitAsync_deduplicates_window_and_preserves_priority_order()
    {
        var provider = new ControlledAudioProvider();
        var controller = new PlaybackPrefetchController(provider);
        var sessionId = Guid.NewGuid();
        var first = CreateRequest(sessionId, 1);
        var duplicate = CreateRequest(sessionId, 1);
        var second = CreateRequest(sessionId, 2);

        await controller.SubmitAsync(
            new PlaybackPrefetchWindow(sessionId, [first, duplicate, second]),
            CancellationToken.None);
        await provider.WaitForCallCountAsync(2);
        await controller.CancelAsync(sessionId, CancellationToken.None);

        Assert.Equal([1, 2], provider.Calls.Select(call => call.Request.SegmentIndex));
        Assert.All(provider.Calls, call => Assert.Equal(PlaybackAudioPriority.Prefetch, call.Priority));
        Assert.All(provider.Calls, call => Assert.False(call.CancellationToken.IsCancellationRequested));
    }

    [Fact]
    public async Task CancelAsync_cancels_active_request_and_pending_window()
    {
        var provider = new ControlledAudioProvider();
        var controller = new PlaybackPrefetchController(provider);
        var sessionId = Guid.NewGuid();
        var pending = provider.EnqueuePending();

        await controller.SubmitAsync(
            new PlaybackPrefetchWindow(sessionId, [CreateRequest(sessionId, 1), CreateRequest(sessionId, 2)]),
            CancellationToken.None);
        await provider.WaitForCallCountAsync(1);

        var cancelTask = controller.CancelAsync(sessionId, CancellationToken.None);
        await pending.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(5));
        await cancelTask;

        Assert.True(provider.Calls[0].CancellationToken.IsCancellationRequested);
        Assert.Single(provider.Calls);
    }

    [Fact]
    public async Task Late_old_session_result_does_not_block_or_enter_new_session_window()
    {
        var provider = new ControlledAudioProvider();
        var controller = new PlaybackPrefetchController(provider);
        var oldSessionId = Guid.NewGuid();
        var oldRequest = CreateRequest(oldSessionId, 1);
        var oldResult = provider.EnqueueLateResult();

        await controller.SubmitAsync(
            new PlaybackPrefetchWindow(oldSessionId, [oldRequest]),
            CancellationToken.None);
        await provider.WaitForCallCountAsync(1);

        var cancelOldSession = controller.CancelAsync(oldSessionId, CancellationToken.None);
        var newSessionId = Guid.NewGuid();
        await controller.SubmitAsync(
            new PlaybackPrefetchWindow(newSessionId, [CreateRequest(newSessionId, 3)]),
            CancellationToken.None);
        await provider.WaitForCallCountAsync(2);

        Assert.Equal(newSessionId, provider.Calls[1].Request.SessionId);
        oldResult.Complete();
        await cancelOldSession;

        await controller.CancelAsync(newSessionId, CancellationToken.None);
    }

    private static PlaybackAudioRequest CreateRequest(Guid sessionId, int segmentIndex)
    {
        var rule = TestHttpTtsRules.Create(
            1,
            "测试规则",
            "https://example.com/tts?text={{speakText}}",
            "audio/mpeg",
            null,
            null,
            null,
            null,
            true,
            null,
            "2026-07-20T00:00:00.0000000Z",
            "2026-07-20T00:00:00.0000000Z");

        return new PlaybackAudioRequest(
            "book-1",
            0,
            segmentIndex,
            $"段落 {segmentIndex}",
            rule.Id,
            rule,
            rule.Normalize(),
            10,
            sessionId);
    }

    private sealed class ControlledAudioProvider : IPlaybackAudioProvider
    {
        private readonly object _syncRoot = new();
        private readonly Queue<Func<CancellationToken, Task<PlaybackAudioResult>>> _responses = [];
        private TaskCompletionSource<bool> _callSignal = CreateSignal();

        public List<Call> Calls { get; } = [];

        public PendingResponse EnqueuePending()
        {
            var completion = new TaskCompletionSource<PlaybackAudioResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellationObserved = CreateSignal();
            _responses.Enqueue(async cancellationToken =>
            {
                using var registration = cancellationToken.Register(() => cancellationObserved.TrySetResult(true));
                try
                {
                    return await completion.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult(true);
                    throw;
                }
            });
            return new PendingResponse(cancellationObserved.Task);
        }

        public LateResponse EnqueueLateResult()
        {
            var completion = new TaskCompletionSource<PlaybackAudioResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _responses.Enqueue(_ => completion.Task);
            return new LateResponse(completion);
        }

        public Task<PlaybackAudioResult> GetAudioAsync(
            PlaybackAudioRequest request,
            PlaybackAudioPriority priority,
            Action<PlaybackAudioProgress>? progressCallback,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> signal;
            lock (_syncRoot)
            {
                Calls.Add(new Call(request, priority, cancellationToken));
                signal = _callSignal;
                _callSignal = CreateSignal();
            }

            signal.TrySetResult(true);
            Func<CancellationToken, Task<PlaybackAudioResult>> response;
            lock (_syncRoot)
            {
                response = _responses.Count > 0
                    ? _responses.Dequeue()
                    : static _ => Task.FromResult(new PlaybackAudioResult("prefetch.mp3", false, null));
            }

            return response(cancellationToken);
        }

        public Task InvalidateAsync(PlaybackAudioRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public async Task WaitForCallCountAsync(int count)
        {
            while (true)
            {
                Task signal;
                lock (_syncRoot)
                {
                    if (Calls.Count >= count)
                    {
                        return;
                    }

                    signal = _callSignal.Task;
                }

                await signal.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        private static TaskCompletionSource<bool> CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public sealed record Call(
            PlaybackAudioRequest Request,
            PlaybackAudioPriority Priority,
            CancellationToken CancellationToken);

        public sealed record PendingResponse(Task CancellationObserved);

        public sealed class LateResponse
        {
            private readonly TaskCompletionSource<PlaybackAudioResult> _completion;

            public LateResponse(TaskCompletionSource<PlaybackAudioResult> completion)
            {
                _completion = completion;
            }

            public void Complete() => _completion.TrySetResult(new PlaybackAudioResult("late.mp3", false, null));
        }
    }
}
