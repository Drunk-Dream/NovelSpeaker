using System.Collections.Concurrent;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Compiles the selected rule, executes HTTP TTS, and returns a local audio file for playback.
/// </summary>
public sealed class PlaybackAudioProvider : IPlaybackAudioProvider
{
    private readonly ITtsRequestCompiler _requestCompiler;
    private readonly IHttpTtsClient _httpTtsClient;
    private readonly IAudioCache _audioCache;
    private readonly ITtsRateLimiter _rateLimiter;
    private readonly ConcurrentDictionary<AudioCacheKey, InFlightOperation> _inFlight = new();
    private readonly ConcurrentDictionary<long, RuleExecutionSlot> _ruleSlots = new();

    public PlaybackAudioProvider(
        ITtsRequestCompiler requestCompiler,
        IHttpTtsClient httpTtsClient,
        IAudioCache audioCache,
        ITtsRateLimiter rateLimiter)
    {
        _requestCompiler = requestCompiler;
        _httpTtsClient = httpTtsClient;
        _audioCache = audioCache;
        _rateLimiter = rateLimiter;
    }

    public async Task<PlaybackAudioResult> GetAudioAsync(
        PlaybackAudioRequest request,
        PlaybackAudioPriority priority,
        Action<PlaybackAudioProgress>? progressCallback,
        CancellationToken cancellationToken)
    {
        var cacheKey = CreateCacheKey(request);
        var cached = await _audioCache.TryGetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
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

            if (priority == PlaybackAudioPriority.Current)
            {
                TryPreemptPrefetch(request.RuleId, cacheKey);
            }

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
        return _audioCache.InvalidateAsync(CreateCacheKey(request), cancellationToken);
    }

    private void TryPreemptPrefetch(long ruleId, AudioCacheKey requestedKey)
    {
        var slot = _ruleSlots.GetOrAdd(ruleId, static _ => new RuleExecutionSlot());
        InFlightOperation? operationToCancel = null;

        lock (slot.SyncRoot)
        {
            if (slot.CurrentOperation is not null &&
                slot.CurrentOperation.Priority == PlaybackAudioPriority.Prefetch &&
                !Equals(slot.CurrentOperation.CacheKey, requestedKey))
            {
                operationToCancel = slot.CurrentOperation;
            }
        }

        operationToCancel?.CancelExecution();
    }

    private async Task<PlaybackAudioResult> ExecuteOperationAsync(
        PlaybackAudioRequest request,
        AudioCacheKey cacheKey,
        InFlightOperation operation)
    {
        var slot = _ruleSlots.GetOrAdd(request.RuleId, static _ => new RuleExecutionSlot());

        try
        {
            await slot.Gate.WaitAsync(operation.ExecutionToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CreateCancelledResult();
        }

        lock (slot.SyncRoot)
        {
            slot.CurrentOperation = operation;
        }

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
                return CreateInvalidRuleResult($"规则模板格式无效：{exception.Message}");
            }

            if (!compilation.IsSuccess)
            {
                return new PlaybackAudioResult(null, false, compilation.Failure);
            }

            while (true)
            {
                try
                {
                    await _rateLimiter.WaitAsync(
                        request.RuleId,
                        request.NormalizedRule.ConcurrentRate,
                        operation.ExecutionToken).ConfigureAwait(false);
                }
                catch (FormatException exception)
                {
                    return CreateInvalidRuleResult($"规则限流格式无效：{exception.Message}");
                }
                catch (OperationCanceledException)
                {
                    return CreateCancelledResult();
                }

                TtsHttpExecutionResult execution;
                try
                {
                    execution = await _httpTtsClient.ExecuteAsync(compilation.Request!, operation.ExecutionToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return CreateCancelledResult();
                }

                if (execution.IsSuccess)
                {
                    var stored = await _audioCache.StoreAsync(
                        new AudioCacheWriteRequest(
                            cacheKey,
                            request.BookId,
                            request.ChapterIndex,
                            request.SegmentIndex,
                            request.RuleId,
                            execution.Audio!.FilePath,
                            execution.Audio.ResponseContentType),
                        operation.ExecutionToken).ConfigureAwait(false);
                    return new PlaybackAudioResult(stored.FilePath, false, null);
                }

                var failure = execution.Failure!;
                if (failure.Kind == TtsErrorKind.RateLimited && failure.RetryAfter is { } retryAfter)
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
        }
        catch (OperationCanceledException)
        {
            return CreateCancelledResult();
        }
        finally
        {
            lock (slot.SyncRoot)
            {
                if (ReferenceEquals(slot.CurrentOperation, operation))
                {
                    slot.CurrentOperation = null;
                }
            }

            slot.Gate.Release();
        }
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

    private static AudioCacheKey CreateCacheKey(PlaybackAudioRequest request)
    {
        return AudioCacheKey.FromPlayback(
            request.BookId,
            request.ChapterIndex,
            request.SegmentIndex,
            request.RuleId,
            request.SpeakSpeed,
            request.SpeechText);
    }

    private sealed class RuleExecutionSlot
    {
        public object SyncRoot { get; } = new();

        public SemaphoreSlim Gate { get; } = new(1, 1);

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

        public Task<PlaybackAudioResult> Task => _completionSource.Task;

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
