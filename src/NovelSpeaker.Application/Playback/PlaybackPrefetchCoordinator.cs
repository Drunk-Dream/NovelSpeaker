using System.Collections.Concurrent;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Owns each session's ordered prefetch window, de-duplication, cancellation and session token.
/// </summary>
internal sealed class PlaybackPrefetchCoordinator : IPlaybackPrefetchController
{
    private readonly IAudioGenerationProvider _audioProvider;
    private readonly ICurrentSpeechProvider _providers;
    private readonly IAppSettingsService _settings;
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();

    public PlaybackPrefetchCoordinator(IAudioGenerationProvider audioProvider, ICurrentSpeechProvider providers,
        IAppSettingsService settings)
    {
        _audioProvider = audioProvider;
        _providers = providers;
        _settings = settings;
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
        if (!state.ReplacePending(window.Revision, window.Requests, window.KeepActiveKey)) return Task.CompletedTask;
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
                var provider = await _providers.GetSelectedProviderAsync(state.ActiveRequestToken).ConfigureAwait(false);
                if (provider is null) continue;
                next = next with { Provider = provider, SpeakSpeed = AppSettings.NormalizeSpeakSpeed(_settings.Current.DefaultSpeakSpeed) };
                await _audioProvider.GetAudioAsync(
                    next,
                    AudioGenerationPriority.Prefetch,
                    progressCallback: null,
                    state.ActiveRequestToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the normal replacement/stop path for best-effort prefetch.
            }
            catch (Exception)
            {
                // Prefetch is best effort; foreground playback owns user-visible failures.
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
        private List<AudioGenerationRequest> _pendingRequests = [];
        private Task? _workerTask;
        private AudioCacheKey? _activeKey;
        private CancellationTokenSource? _activeRequestCts;
        private bool _workerRunning;
        private long _windowRevision = -1;

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

        public bool ReplacePending(
            long revision,
            IReadOnlyList<AudioGenerationRequest> requests,
            AudioCacheKey? keepActiveKey)
        {
            lock (_syncRoot)
            {
                if (revision < _windowRevision) return false;
                _windowRevision = revision;
                var desired = Deduplicate(requests);
                if (_activeKey is not null)
                {
                    var keepActive = keepActiveKey == _activeKey ||
                        desired.Any(request => request.ToCacheKey() == _activeKey);
                    if (!keepActive)
                    {
                        _activeRequestCts?.Cancel();
                    }

                    desired = desired
                        .Where(request => request.ToCacheKey() != _activeKey)
                        .ToList();
                }

                _pendingRequests = desired;
                return true;
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

        public AudioGenerationRequest? TryDequeueNext()
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

        private static List<AudioGenerationRequest> Deduplicate(IReadOnlyList<AudioGenerationRequest> requests)
        {
            var seen = new HashSet<AudioCacheKey>();
            var result = new List<AudioGenerationRequest>(requests.Count);
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
