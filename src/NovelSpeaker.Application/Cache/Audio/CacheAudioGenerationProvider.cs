using System.Collections.Concurrent;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Cache.Audio;

/// <summary>
/// Executes the bound Provider runtime, and returns a local audio file for playback.
/// </summary>
public sealed class CacheAudioGenerationProvider : IAudioGenerationProvider
{
    private readonly IGeneratedAudioFileStore _files;
    private readonly IAudioCache _audioCache;
    private readonly IAudioGenerationFailureReporter? _failureReporter;
    private readonly IObservability _observability;
    private readonly ConcurrentDictionary<AudioCacheKey, InFlightOperation> _inFlight = new();

    public CacheAudioGenerationProvider(
        IGeneratedAudioFileStore files,
        IAudioCache audioCache,
        IAudioGenerationFailureReporter? failureReporter = null,
        IObservability? observability = null)
    {
        _files = files;
        _audioCache = audioCache;
        _failureReporter = failureReporter;
        _observability = observability ?? new ObservabilityHub(new ObservabilityContextAccessor());
    }

    public async Task<AudioGenerationResult> GetAudioAsync(
        AudioGenerationRequest request,
        AudioGenerationPriority priority,
        Action<AudioGenerationProgress>? progressCallback,
        CancellationToken cancellationToken)
    {
        using var operation = _observability.StartOperation(OperationCatalog.CacheAudioGeneration);
        try
        {
            var result = await GetAudioCoreAsync(request, priority, progressCallback, cancellationToken)
                .ConfigureAwait(false);
            operation.Complete(result.IsSuccess
                ? OperationResult.Succeeded()
                : result.Failure?.Kind == TtsErrorKind.Cancelled
                    ? OperationResult.Cancelled()
                    : OperationResult.Failed("cache-failed"));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation.Complete(OperationResult.Cancelled());
            throw;
        }
        catch
        {
            operation.Complete(OperationResult.Failed("cache-failed"));
            throw;
        }
    }

    private async Task<AudioGenerationResult> GetAudioCoreAsync(
        AudioGenerationRequest request,
        AudioGenerationPriority priority,
        Action<AudioGenerationProgress>? progressCallback,
        CancellationToken cancellationToken)
    {
        var cacheKey = request.ToCacheKey();
        AudioCacheEntry? cached;
        try
        {
            cached = await _audioCache.TryGetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(request, exception, "Playback audio cache lookup");
            return CreateUnexpectedFailureResult();
        }

        if (cached is not null)
        {
            return new AudioGenerationResult(cached.FilePath, true, null);
        }

        while (true)
        {
            if (_inFlight.TryGetValue(cacheKey, out var existing))
            {
                existing.RegisterListener(progressCallback);
                if (priority == AudioGenerationPriority.Current)
                {
                    existing.PromoteToCurrent();
                }

                var result = await existing.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (priority == AudioGenerationPriority.Current && existing.ExecutionToken.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested && result.Failure?.Kind == TtsErrorKind.Cancelled)
                {
                    _inFlight.TryRemove(new KeyValuePair<AudioCacheKey, InFlightOperation>(cacheKey, existing));
                    continue;
                }
                return result;
            }

            TryPreemptLowerPriority(request.Provider.ProviderId, cacheKey, priority);

            var operation = new InFlightOperation(request.Provider.ProviderId, cacheKey, priority, cancellationToken);
            operation.RegisterListener(progressCallback);
            if (!_inFlight.TryAdd(cacheKey, operation))
            {
                operation.Dispose();
                continue;
            }

            operation.Start(
                () => ExecuteOperationAsync(request, cacheKey, operation),
                () => _inFlight.TryRemove(new KeyValuePair<AudioCacheKey, InFlightOperation>(cacheKey, operation)));

            return await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task InvalidateAsync(AudioGenerationRequest request, CancellationToken cancellationToken)
    {
        return _audioCache.InvalidateAsync(request.ToCacheKey(), cancellationToken);
    }

    private void TryPreemptLowerPriority(
        ProviderId providerId,
        AudioCacheKey requestedKey,
        AudioGenerationPriority requestedPriority)
    {
        foreach (var operation in _inFlight.Values)
        {
            if (operation.ProviderId == providerId && operation.Priority < requestedPriority &&
                !Equals(operation.CacheKey, requestedKey))
            {
                operation.CancelExecution();
            }
        }
    }

    private async Task<AudioGenerationResult> ExecuteOperationAsync(
        AudioGenerationRequest request,
        AudioCacheKey cacheKey,
        InFlightOperation operation)
    {
        try
        {
            var cached = await _audioCache.TryGetAsync(cacheKey, operation.ExecutionToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return new AudioGenerationResult(cached.FilePath, true, null);
            }

            const int maxRateLimitRetries = 2;
            const int maxTransientRetries = 2;
            var rateLimitRetries = 0;
            var transientRetries = 0;
            while (true)
            {
                var synthesis = await request.Provider.Runtime.SynthesizeAsync(
                    request.Provider.Provider,
                    new ProviderSynthesisRequest(request.SpeechText, request.SpeakSpeed, MapAdmissionPriority(operation.Priority)),
                    operation.ExecutionToken).ConfigureAwait(false);
                await using var stream = synthesis.Audio;
                operation.ExecutionToken.ThrowIfCancellationRequested();
                if (synthesis.Failure is { Kind: ProviderSynthesisFailureKind.RateLimited, RetryAfter: { } retryAfter } &&
                    operation.Priority == AudioGenerationPriority.Current &&
                    rateLimitRetries < maxRateLimitRetries)
                {
                    rateLimitRetries++;
                    operation.ReportProgress(new AudioGenerationProgress(BuildRateLimitedMessage(retryAfter), retryAfter));
                    continue;
                }
                // HTTP transport already owns its bounded network and 5xx retries.
                // Other providers share this synthesis-level transient retry boundary.
                if (request.Provider.Provider.Type != SpeechProviderType.Http &&
                    synthesis.Failure?.Kind is ProviderSynthesisFailureKind.Network or ProviderSynthesisFailureKind.Timeout &&
                    transientRetries < maxTransientRetries)
                {
                    transientRetries++;
                    continue;
                }
                if (!synthesis.IsSuccess)
                {
                    var kind = synthesis.Failure?.Kind switch
                    {
                        ProviderSynthesisFailureKind.ProviderUnavailable or ProviderSynthesisFailureKind.InvalidRequest => TtsErrorKind.InvalidRule,
                        ProviderSynthesisFailureKind.Network => TtsErrorKind.Network,
                        ProviderSynthesisFailureKind.Timeout => TtsErrorKind.Timeout,
                        ProviderSynthesisFailureKind.RateLimited => TtsErrorKind.RateLimited,
                        ProviderSynthesisFailureKind.EmptyAudio => TtsErrorKind.EmptyAudioResponse,
                        ProviderSynthesisFailureKind.InvalidAudio => TtsErrorKind.AudioDecode,
                        ProviderSynthesisFailureKind.Cancelled => TtsErrorKind.Cancelled,
                        _ => TtsErrorKind.Unknown
                    };
                    return new AudioGenerationResult(null, false,
                        new TtsExecutionFailure(kind, synthesis.Failure?.Message ?? "语音服务合成失败。", null, null, null, null));
                }

                await using var audio = await _files.WriteAsync(synthesis, operation.ExecutionToken).ConfigureAwait(false);
                var stored = await _audioCache.StoreAsync(new AudioCacheWriteRequest(
                    cacheKey, request.BookId, request.ChapterIndex, 0, audio.FilePath, audio.ResponseContentType),
                    operation.ExecutionToken).ConfigureAwait(false);
                return new AudioGenerationResult(stored.FilePath, false, null);
            }
        }
        catch (OperationCanceledException)
        {
            return CreateCancelledResult();
        }
        catch (Exception exception)
        {
            LogFailure(request, exception, "Playback audio generation");
            return CreateUnexpectedFailureResult();
        }
    }

    private static TtsAdmissionPriority MapAdmissionPriority(AudioGenerationPriority priority) =>
        priority switch
        {
            AudioGenerationPriority.Current => TtsAdmissionPriority.CurrentPlayback,
            AudioGenerationPriority.Prefetch => TtsAdmissionPriority.Prefetch,
            AudioGenerationPriority.ActiveCache => TtsAdmissionPriority.ActiveCache,
            _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, null)
        };

    private void LogFailure(AudioGenerationRequest request, Exception exception, string operation)
    {
        try { _failureReporter?.Report(operation, exception, request); }
        catch { /* Diagnostics must not replace the synthesis result. */ }
    }

    private static string BuildRateLimitedMessage(TimeSpan retryAfter)
    {
        return retryAfter > TimeSpan.Zero
            ? $"请求过于频繁，正在等待 {retryAfter.TotalSeconds:0.#} 秒后重试。"
            : "请求过于频繁，正在等待后重试。";
    }

    private static AudioGenerationResult CreateCancelledResult()
    {
        return new AudioGenerationResult(
            null,
            false,
            new TtsExecutionFailure(TtsErrorKind.Cancelled, "已取消当前音频生成。", null, null, null, null));
    }

    private static AudioGenerationResult CreateUnexpectedFailureResult()
    {
        return new AudioGenerationResult(
            null,
            false,
            new TtsExecutionFailure(
                TtsErrorKind.Unknown,
                "音频生成失败，请稍后重试。",
                null,
                null,
                null,
                null));
    }

    private sealed class InFlightOperation : IDisposable
    {
        private readonly CancellationTokenSource _executionCts;
        private readonly CancellationToken _executionToken;
        private readonly TaskCompletionSource<AudioGenerationResult> _completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _syncRoot = new();
        private AudioGenerationProgress? _lastProgress;
        private Action<AudioGenerationProgress>? _listeners;

        public InFlightOperation(
            ProviderId providerId,
            AudioCacheKey cacheKey,
            AudioGenerationPriority priority,
            CancellationToken ownerCancellationToken)
        {
            ProviderId = providerId;
            CacheKey = cacheKey;
            Priority = priority;
            _executionCts = CancellationTokenSource.CreateLinkedTokenSource(ownerCancellationToken);
            _executionToken = _executionCts.Token;
        }

        public ProviderId ProviderId { get; }

        public AudioCacheKey CacheKey { get; }

        public AudioGenerationPriority Priority { get; private set; }

        public CancellationToken ExecutionToken => _executionToken;

        public void PromoteToCurrent()
        {
            Priority = AudioGenerationPriority.Current;
        }

        public void Dispose() => _executionCts.Dispose();

        public void RegisterListener(Action<AudioGenerationProgress>? listener)
        {
            if (listener is null)
            {
                return;
            }

            AudioGenerationProgress? lastProgress;
            lock (_syncRoot)
            {
                _listeners += listener;
                lastProgress = _lastProgress;
            }

            if (lastProgress is not null)
            {
                listener(lastProgress);
            }
        }

        public void ReportProgress(AudioGenerationProgress progress)
        {
            Action<AudioGenerationProgress>? listeners;
            lock (_syncRoot)
            {
                _lastProgress = progress;
                listeners = _listeners;
            }

            listeners?.Invoke(progress);
        }

        public void Start(Func<Task<AudioGenerationResult>> factory, Action onCompleted)
        {
            _ = RunAsync(factory, onCompleted);
        }

        public async Task<AudioGenerationResult> WaitAsync(CancellationToken cancellationToken)
        {
            return await _completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void CancelExecution()
        {
            lock (_syncRoot)
            {
                if (!_completionSource.Task.IsCompleted) _executionCts.Cancel();
            }
        }

        private async Task RunAsync(Func<Task<AudioGenerationResult>> factory, Action onCompleted)
        {
            try
            {
                var result = await factory().ConfigureAwait(false);
                _completionSource.TrySetResult(result);
            }
            catch (Exception exception)
            {
                _completionSource.TrySetException(exception);
            }
            finally
            {
                onCompleted();
                lock (_syncRoot) { _executionCts.Dispose(); }
            }
        }
    }
}
