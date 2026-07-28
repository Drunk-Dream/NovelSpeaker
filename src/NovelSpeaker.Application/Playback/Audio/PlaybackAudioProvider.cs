using System.Collections.Concurrent;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Playback.Audio;

/// <summary>
/// Compiles the selected rule, executes HTTP TTS, and returns a local audio file for playback.
/// </summary>
public sealed class PlaybackAudioProvider : IPlaybackAudioProvider
{
    private readonly ITtsRequestCompiler _requestCompiler;
    private readonly IHttpTtsClient _httpTtsClient;
    private readonly IAudioCache _audioCache;
    private readonly ITtsRateLimiter _rateLimiter;
    private readonly IPlaybackAudioFailureReporter? _failureReporter;
    private readonly ConcurrentDictionary<AudioCacheKey, InFlightOperation> _inFlight = new();
    private readonly ConcurrentDictionary<long, RuleExecutionState> _ruleExecutions = new();

    public PlaybackAudioProvider(
        ITtsRequestCompiler requestCompiler,
        IHttpTtsClient httpTtsClient,
        IAudioCache audioCache,
        ITtsRateLimiter rateLimiter,
        IPlaybackAudioFailureReporter? failureReporter = null)
    {
        _requestCompiler = requestCompiler;
        _httpTtsClient = httpTtsClient;
        _audioCache = audioCache;
        _rateLimiter = rateLimiter;
        _failureReporter = failureReporter;
    }

    public async Task<PlaybackAudioResult> GetAudioAsync(
        PlaybackAudioRequest request,
        PlaybackAudioPriority priority,
        Action<PlaybackAudioProgress>? progressCallback,
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
            return new PlaybackAudioResult(cached.FilePath, true, null);
        }

        while (true)
        {
            if (_inFlight.TryGetValue(cacheKey, out var existing))
            {
                existing.RegisterListener(progressCallback);
                if (priority == PlaybackAudioPriority.Current)
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

    public Task InvalidateAsync(PlaybackAudioRequest request, CancellationToken cancellationToken)
    {
        return _audioCache.InvalidateAsync(request.ToCacheKey(), cancellationToken);
    }

    private void TryPreemptLowerPriority(
        long ruleId,
        AudioCacheKey requestedKey,
        PlaybackAudioPriority requestedPriority)
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

    private async Task<PlaybackAudioResult> ExecuteOperationAsync(
        PlaybackAudioRequest request,
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
                return new PlaybackAudioResult(cached.FilePath, true, null);
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
                return new PlaybackAudioResult(null, false, compilation.Failure);
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
                                return new PlaybackAudioResult(stored.FilePath, false, null);
                            }
                        }

                        var failure = execution.Failure!;
                        if (failure.Kind == TtsErrorKind.RateLimited &&
                            failure.RetryAfter is { } retryAfter)
                        {
                            _rateLimiter.ApplyRetryAfter(request.RuleId, retryAfter);
                            if (operation.Priority == PlaybackAudioPriority.Current)
                            {
                                operation.ReportProgress(new PlaybackAudioProgress(
                                    BuildRateLimitedMessage(retryAfter),
                                    retryAfter));
                                continue;
                            }
                        }

                        return new PlaybackAudioResult(null, false, failure);
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

    private static TtsAdmissionPriority MapAdmissionPriority(PlaybackAudioPriority priority) =>
        priority switch
        {
            PlaybackAudioPriority.Current => TtsAdmissionPriority.CurrentPlayback,
            PlaybackAudioPriority.Prefetch => TtsAdmissionPriority.Prefetch,
            PlaybackAudioPriority.ActiveCache => TtsAdmissionPriority.ActiveCache,
            _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, null)
        };

    private void LogFailure(PlaybackAudioRequest request, Exception exception, string operation)
    {
        _failureReporter?.Report(operation, exception, request);
    }

    private static string BuildRateLimitedMessage(TimeSpan retryAfter)
    {
        return retryAfter > TimeSpan.Zero
            ? $"请求过于频繁，正在等待 {retryAfter.TotalSeconds:0.#} 秒后重试。"
            : "请求过于频繁，正在等待后重试。";
    }

    private static PlaybackAudioResult CreateCancelledResult()
    {
        return new PlaybackAudioResult(
            null,
            false,
            new TtsExecutionFailure(TtsErrorKind.Cancelled, "已取消当前音频生成。", null, null, null, null));
    }

    private static PlaybackAudioResult CreateInvalidRuleResult(string message)
    {
        return new PlaybackAudioResult(
            null,
            false,
            new TtsExecutionFailure(TtsErrorKind.InvalidRule, message, null, null, null, null));
    }

    private static PlaybackAudioResult CreateUnexpectedFailureResult()
    {
        return new PlaybackAudioResult(
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
        private readonly TaskCompletionSource<PlaybackAudioResult> _completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _syncRoot = new();
        private PlaybackAudioProgress? _lastProgress;
        private Action<PlaybackAudioProgress>? _listeners;

        public InFlightOperation(
            long ruleId,
            AudioCacheKey cacheKey,
            PlaybackAudioPriority priority,
            CancellationToken ownerCancellationToken)
        {
            RuleId = ruleId;
            CacheKey = cacheKey;
            Priority = priority;
            _executionCts = CancellationTokenSource.CreateLinkedTokenSource(ownerCancellationToken);
        }

        public long RuleId { get; }

        public AudioCacheKey CacheKey { get; }

        public PlaybackAudioPriority Priority { get; private set; }

        public CancellationToken ExecutionToken => _executionCts.Token;

        public void PromoteToCurrent()
        {
            Priority = PlaybackAudioPriority.Current;
        }

        public void RegisterListener(Action<PlaybackAudioProgress>? listener)
        {
            if (listener is null)
            {
                return;
            }

            PlaybackAudioProgress? lastProgress;
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

        public void ReportProgress(PlaybackAudioProgress progress)
        {
            Action<PlaybackAudioProgress>? listeners;
            lock (_syncRoot)
            {
                _lastProgress = progress;
                listeners = _listeners;
            }

            listeners?.Invoke(progress);
        }

        public void Start(Func<Task<PlaybackAudioResult>> factory, Action onCompleted)
        {
            _ = RunAsync(factory, onCompleted);
        }

        public async Task<PlaybackAudioResult> WaitAsync(CancellationToken cancellationToken)
        {
            return await _completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void CancelExecution()
        {
            _executionCts.Cancel();
        }

        private async Task RunAsync(Func<Task<PlaybackAudioResult>> factory, Action onCompleted)
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
