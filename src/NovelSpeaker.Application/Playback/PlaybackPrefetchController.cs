using System.Collections.Concurrent;
using NovelSpeaker.Application.Playback.Cache;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Owns each session's ordered prefetch window, de-duplication, cancellation and session token.
/// </summary>
public sealed class PlaybackPrefetchController : IPlaybackPrefetchController
{
    private readonly IPlaybackAudioProvider _audioProvider;
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();

    public PlaybackPrefetchController(IPlaybackAudioProvider audioProvider)
    {
        _audioProvider = audioProvider;
    }

    public Task SubmitAsync(PlaybackPrefetchWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();
        if (window.SessionId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        foreach (var request in window.Requests)
        {
            if (request.SessionId != window.SessionId)
            {
                throw new ArgumentException("预取请求必须属于提交窗口的会话。", nameof(window));
            }
        }

        var state = _sessions.GetOrAdd(window.SessionId, static _ => new SessionState());
        state.ReplacePending(window.Requests);
        state.EnsureWorkerStarted(() => RunSessionAsync(state));
        return Task.CompletedTask;
    }

    public async Task CancelAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sessionId == Guid.Empty)
        {
            return;
        }

        if (_sessions.TryRemove(sessionId, out var state))
        {
            await state.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task RunSessionAsync(SessionState state)
    {
        while (true)
        {
            var next = state.TryDequeueNext();
            if (next is null)
            {
                state.MarkWorkerStopped();
                if (!state.HasPendingWork)
                {
                    return;
                }

                state.EnsureWorkerStarted(() => RunSessionAsync(state));
                return;
            }

            try
            {
                await _audioProvider.GetAudioAsync(
                    next,
                    PlaybackAudioPriority.Prefetch,
                    progressCallback: null,
                    state.ActiveRequestToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the normal replacement/stop path for best-effort prefetch.
            }
            finally
            {
                state.CompleteActiveRequest();
            }
        }
    }

    private sealed class SessionState
    {
        private readonly object _syncRoot = new();
        private readonly CancellationTokenSource _sessionCts = new();
        private List<PlaybackAudioRequest> _pendingRequests = [];
        private Task? _workerTask;
        private AudioCacheKey? _activeKey;
        private CancellationTokenSource? _activeRequestCts;
        private bool _workerRunning;

        public bool HasPendingWork
        {
            get
            {
                lock (_syncRoot)
                {
                    return _pendingRequests.Count > 0 || _activeRequestCts is not null;
                }
            }
        }

        public CancellationToken ActiveRequestToken
        {
            get
            {
                lock (_syncRoot)
                {
                    return _activeRequestCts?.Token ?? _sessionCts.Token;
                }
            }
        }

        public void ReplacePending(IReadOnlyList<PlaybackAudioRequest> requests)
        {
            lock (_syncRoot)
            {
                var desired = Deduplicate(requests);
                if (_activeKey is not null)
                {
                    var keepActive = desired.Any(request => request.ToCacheKey() == _activeKey);
                    if (!keepActive)
                    {
                        _activeRequestCts?.Cancel();
                    }

                    desired = desired
                        .Where(request => request.ToCacheKey() != _activeKey)
                        .ToList();
                }

                _pendingRequests = desired;
            }
        }

        public void EnsureWorkerStarted(Func<Task> workerFactory)
        {
            lock (_syncRoot)
            {
                if (_workerRunning || (_pendingRequests.Count == 0 && _activeRequestCts is null))
                {
                    return;
                }

                _workerRunning = true;
                _workerTask = Task.Run(workerFactory);
            }
        }

        public PlaybackAudioRequest? TryDequeueNext()
        {
            lock (_syncRoot)
            {
                if (_sessionCts.IsCancellationRequested || _pendingRequests.Count == 0)
                {
                    return null;
                }

                var next = _pendingRequests[0];
                _pendingRequests.RemoveAt(0);
                _activeKey = next.ToCacheKey();
                _activeRequestCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token);
                return next;
            }
        }

        public void CompleteActiveRequest()
        {
            CancellationTokenSource? toDispose;
            lock (_syncRoot)
            {
                toDispose = _activeRequestCts;
                _activeRequestCts = null;
                _activeKey = null;
            }

            toDispose?.Dispose();
        }

        public void MarkWorkerStopped()
        {
            lock (_syncRoot)
            {
                _workerRunning = false;
            }
        }

        public async Task CancelAsync()
        {
            Task? worker;
            CancellationTokenSource? activeRequest;
            lock (_syncRoot)
            {
                _sessionCts.Cancel();
                _pendingRequests = [];
                activeRequest = _activeRequestCts;
                worker = _workerTask;
            }

            activeRequest?.Cancel();
            if (worker is not null)
            {
                try
                {
                    await worker.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            activeRequest?.Dispose();
            _sessionCts.Dispose();
        }

        private static List<PlaybackAudioRequest> Deduplicate(IReadOnlyList<PlaybackAudioRequest> requests)
        {
            var seen = new HashSet<AudioCacheKey>();
            var result = new List<PlaybackAudioRequest>(requests.Count);
            foreach (var request in requests)
            {
                if (seen.Add(request.ToCacheKey()))
                {
                    result.Add(request);
                }
            }

            return result;
        }
    }
}
