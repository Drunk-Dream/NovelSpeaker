using System.Collections.Concurrent;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Cache.Audio;

/// <summary>
/// Compiles the selected rule, executes HTTP TTS, and returns a local audio file for playback.
/// </summary>
public sealed class CacheAudioGenerationProvider : IAudioGenerationProvider
{
    private readonly ITtsRequestCompiler _requestCompiler;
    private readonly IHttpTtsClient _httpTtsClient;
    private readonly IAudioCache _audioCache;
    private readonly ITtsRateLimiter _rateLimiter;
    private readonly IAudioGenerationFailureReporter? _failureReporter;
    private readonly IObservability _observability;
    private readonly ConcurrentDictionary<AudioCacheKey, InFlightOperation> _inFlight = new();
    private readonly ConcurrentDictionary<long, RuleExecutionState> _ruleExecutions = new();

    public CacheAudioGenerationProvider(
        ITtsRequestCompiler requestCompiler,
        IHttpTtsClient httpTtsClient,
        IAudioCache audioCache,
        ITtsRateLimiter rateLimiter,
        IAudioGenerationFailureReporter? failureReporter = null,
        IObservability? observability = null)
    {
        _requestCompiler = requestCompiler;
        _httpTtsClient = httpTtsClient;
        _audioCache = audioCache;
        _rateLimiter = rateLimiter;
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

                return await existing.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            TryPreemptLowerPriority(request.RuleId, cacheKey, priority);

            var operation = new InFlightOperation(request.RuleId, cacheKey, priority, cancellationToken);
            operation.RegisterListener(progressCallback);
            if (!_inFlight.TryAdd(cacheKey, operation))
            {
                continue;
            }

            operation.Start(
                () => ExecuteOperationAsync(request, cacheKey, operation),
                () => _inFlight.TryRemove(cacheKey, out _));

            return await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task InvalidateAsync(AudioGenerationRequest request, CancellationToken cancellationToken)
    {
        return _audioCache.InvalidateAsync(request.ToCacheKey(), cancellationToken);
    }

    private void TryPreemptLowerPriority(
        long ruleId,
        AudioCacheKey requestedKey,
        AudioGenerationPriority requestedPriority)
    {
        var executionState = _ruleExecutions.GetOrAdd(ruleId, static _ => new RuleExecutionState());
        InFlightOperation? operationToCancel = null;

        lock (executionState.SyncRoot)
        {
            if (executionState.CurrentOperation is not null &&
                executionState.CurrentOperation.Priority < requestedPriority &&
                !Equals(executionState.CurrentOperation.CacheKey, requestedKey))
            {
                operationToCancel = executionState.CurrentOperation;
            }
        }

        operationToCancel?.CancelExecution();
    }

    private async Task<AudioGenerationResult> ExecuteOperationAsync(
        AudioGenerationRequest request,
        AudioCacheKey cacheKey,
        InFlightOperation operation)
    {
        var executionState = _ruleExecutions.GetOrAdd(
            request.RuleId,
            static _ => new RuleExecutionState());

        try
        {
            var cached = await _audioCache.TryGetAsync(cacheKey, operation.ExecutionToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return new AudioGenerationResult(cached.FilePath, true, null);
            }

            TtsRequestCompilationResult compilation;
            try
            {
                compilation = await _requestCompiler.CompileAsync(
                    request.NormalizedRule,
                    new TtsRuleContext(
                        request.SpeechText,
                        request.SpeakSpeed,
                        request.SourceRule),
                    operation.ExecutionToken).ConfigureAwait(false);
            }
            catch (FormatException exception)
            {
                LogFailure(request, exception, "Playback TTS rule normalization");
                return CreateInvalidRuleResult("规则模板格式无效，请检查规则后重试。");
            }

            if (!compilation.IsSuccess)
            {
                return new AudioGenerationResult(null, false, compilation.Failure);
            }

            while (true)
            {
                ITtsAdmissionLease admission;
                try
                {
                    admission = await _rateLimiter.AcquireAsync(
                        request.RuleId,
                        request.NormalizedRule.ConcurrentRate,
                        MapAdmissionPriority(operation.Priority),
                        operation.ExecutionToken).ConfigureAwait(false);
                }
                catch (FormatException exception)
                {
                    LogFailure(request, exception, "Playback TTS rate limit parsing");
                    return CreateInvalidRuleResult("规则限流格式无效，请检查规则后重试。");
                }
                catch (OperationCanceledException)
                {
                    return CreateCancelledResult();
                }

                await using (admission.ConfigureAwait(false))
                {
                    lock (executionState.SyncRoot)
                    {
                        executionState.CurrentOperation = operation;
                    }

                    try
                    {
                        TtsHttpExecutionResult execution;
                        try
                        {
                            execution = await _httpTtsClient
                                .ExecuteAsync(compilation.Request!, operation.ExecutionToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return CreateCancelledResult();
                        }

                        if (execution.IsSuccess)
                        {
                            var audio = execution.Audio!;
                            await using (audio.ConfigureAwait(false))
                            {
                                var stored = await _audioCache.StoreAsync(
                                    new AudioCacheWriteRequest(
                                        cacheKey,
                                        request.BookId,
                                        request.ChapterIndex,
                                        request.RuleId,
                                        audio.FilePath,
                                        audio.ResponseContentType),
                                    operation.ExecutionToken).ConfigureAwait(false);
                                return new AudioGenerationResult(stored.FilePath, false, null);
                            }
                        }

                        var failure = execution.Failure!;
                        if (failure.Kind == TtsErrorKind.RateLimited &&
                            failure.RetryAfter is { } retryAfter)
                        {
                            _rateLimiter.ApplyRetryAfter(request.RuleId, retryAfter);
                            if (operation.Priority == AudioGenerationPriority.Current)
                            {
                                operation.ReportProgress(new AudioGenerationProgress(
                                    BuildRateLimitedMessage(retryAfter),
                                    retryAfter));
                                continue;
                            }
                        }

                        return new AudioGenerationResult(null, false, failure);
                    }
                    finally
                    {
                        lock (executionState.SyncRoot)
                        {
                            if (ReferenceEquals(executionState.CurrentOperation, operation))
                            {
                                executionState.CurrentOperation = null;
                            }
                        }
                    }
                }
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
        _failureReporter?.Report(operation, exception, request);
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

    private static AudioGenerationResult CreateInvalidRuleResult(string message)
    {
        return new AudioGenerationResult(
            null,
            false,
            new TtsExecutionFailure(TtsErrorKind.InvalidRule, message, null, null, null, null));
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

    private sealed class RuleExecutionState
    {
        public object SyncRoot { get; } = new();

        public InFlightOperation? CurrentOperation { get; set; }
    }

    private sealed class InFlightOperation
    {
        private readonly CancellationTokenSource _executionCts;
        private readonly TaskCompletionSource<AudioGenerationResult> _completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _syncRoot = new();
        private AudioGenerationProgress? _lastProgress;
        private Action<AudioGenerationProgress>? _listeners;

        public InFlightOperation(
            long ruleId,
            AudioCacheKey cacheKey,
            AudioGenerationPriority priority,
            CancellationToken ownerCancellationToken)
        {
            RuleId = ruleId;
            CacheKey = cacheKey;
            Priority = priority;
            _executionCts = CancellationTokenSource.CreateLinkedTokenSource(ownerCancellationToken);
        }

        public long RuleId { get; }

        public AudioCacheKey CacheKey { get; }

        public AudioGenerationPriority Priority { get; private set; }

        public CancellationToken ExecutionToken => _executionCts.Token;

        public void PromoteToCurrent()
        {
            Priority = AudioGenerationPriority.Current;
        }

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
            _executionCts.Cancel();
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
                _executionCts.Dispose();
                onCompleted();
            }
        }
    }
}
