using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Cache;

/// <summary>One process-owned worker, retaining only current window identities and attempts.</summary>
internal sealed class SpeechPlanRepairCoordinator : ISpeechPlanRepairCoordinator
{
    private readonly IBookPlaybackContentService _content;
    private readonly IBookPlaybackMetadataQuery _metadata;
    private readonly IAudioCacheStore _audio;
    private readonly IChapterSpeechPlanStore _plans;
    private readonly IAppSettingsService _settings;
    private readonly IRegexReplacementRuleRepository _rules;
    private readonly ICacheInvalidationCoordinator _invalidation;
    private readonly ICacheCompletenessFailureReporter? _failureReporter;
    private readonly object _gate = new();
    private readonly Dictionary<CancellationToken, Demand> _demands = [];
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private SpeechPlanRepairRequest? _running;
    private CancellationTokenSource? _runningCancellation;
    private bool _stopping;

    public SpeechPlanRepairCoordinator(
        IBookPlaybackContentService contentService,
        IBookPlaybackMetadataQuery metadata,
        IAudioCacheStore audioCacheStore,
        IChapterSpeechPlanStore speechPlanStore,
        IAppSettingsService settingsService,
        IRegexReplacementRuleRepository regexRuleRepository,
        ICacheInvalidationCoordinator invalidationCoordinator,
        ICacheCompletenessFailureReporter? failureReporter = null)
    {
        _content = contentService;
        _metadata = metadata;
        _audio = audioCacheStore;
        _plans = speechPlanStore;
        _settings = settingsService;
        _rules = regexRuleRepository;
        _invalidation = invalidationCoordinator;
        _failureReporter = failureReporter;
        _worker = Task.Run(RunAsync);
    }

    public void UpdateDemand(IReadOnlyList<SpeechPlanRepairRequest> requests, CancellationToken visibilityToken)
    {
        lock (_gate)
        {
            if (_stopping || !visibilityToken.CanBeCanceled || visibilityToken.IsCancellationRequested) return;
            if (!_demands.TryGetValue(visibilityToken, out var demand))
            {
                demand = new Demand();
                _demands.Add(visibilityToken, demand);
                demand.Registration = visibilityToken.Register(() => Withdraw(visibilityToken));
                if (visibilityToken.IsCancellationRequested) return;
            }

            demand.Requests = requests.Distinct().ToArray();
            demand.Attempted.IntersectWith(demand.Requests);
            if (_running is { } running && _runningCancellation?.IsCancellationRequested == false &&
                demand.Requests.Contains(running))
                demand.Attempted.Add(running);
            CancelUnneededWork();
            if (_wake.CurrentCount == 0) _wake.Release();
        }
    }

    private void Withdraw(CancellationToken visibilityToken)
    {
        lock (_gate)
        {
            if (_demands.Remove(visibilityToken, out var demand)) demand.Registration.Unregister();
            CancelUnneededWork();
        }
    }

    private void CancelUnneededWork()
    {
        if (_running is { } running && !_demands.Values.Any(demand => demand.Requests.Contains(running)))
            _runningCancellation?.Cancel();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_stopping)
            {
                _stopping = true;
                foreach (var demand in _demands.Values) demand.Registration.Unregister();
                _demands.Clear();
                _lifetime.Cancel();
            }
        }
        await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await StopAsync(budget.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (budget.IsCancellationRequested) { }
        // A late worker owns disposal of the resources it still uses.
    }

    private async Task RunAsync()
    {
        var lifetimeToken = _lifetime.Token;
        try
        {
            while (true)
            {
                await _wake.WaitAsync(lifetimeToken).ConfigureAwait(false);
                while (true)
                {
                    SpeechPlanRepairRequest? request;
                    CancellationTokenSource cancellation;
                    lock (_gate)
                    {
                        if (_stopping) return;
                        request = _demands.Values.SelectMany(demand => demand.Requests
                            .Where(candidate => !demand.Attempted.Contains(candidate))).FirstOrDefault();
                        if (request is null) break;
                        foreach (var demand in _demands.Values.Where(demand => demand.Requests.Contains(request)))
                            demand.Attempted.Add(request);
                        _running = request;
                        cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
                        _runningCancellation = cancellation;
                    }
                    try { await RepairAsync(request, cancellation.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                    catch (Exception exception) { ReportFailure(exception); }
                    finally
                    {
                        lock (_gate)
                        {
                            _running = null;
                            _runningCancellation = null;
                            cancellation.Dispose();
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
        catch (Exception exception) { ReportFailure(exception); }
        finally
        {
            lock (_gate)
            {
                _stopping = true;
                foreach (var demand in _demands.Values) demand.Registration.Unregister();
                _demands.Clear();
            }
            _wake.Dispose();
            _lifetime.Dispose();
        }
    }

    private async Task RepairAsync(SpeechPlanRepairRequest request, CancellationToken token)
    {
        if (await _audio.GetChapterAsync(request.BookId, request.ChapterIndex, token).ConfigureAwait(false) is null)
            return;
        var metadata = await _metadata.GetChapterAsync(request.BookId, request.ChapterIndex, token).ConfigureAwait(false);
        if (metadata?.ChapterId != request.ChapterId) return;
        var profile = await GetProfileAsync(token).ConfigureAwait(false);
        if (request.TargetTextProfile is { } target && !target.Equals(profile)) return;
        var chapter = await _content.GetChapterAsync(request.BookId, request.ChapterIndex, token).ConfigureAwait(false);
        if (chapter?.ChapterId != request.ChapterId || chapter.SpeechPlan is not { } plan ||
            !plan.TextProfileFingerprint.Equals(profile) ||
            !profile.Equals(await GetProfileAsync(token).ConfigureAwait(false))) return;
        var current = await _metadata.GetChapterAsync(request.BookId, request.ChapterIndex, token).ConfigureAwait(false);
        if (current?.ChapterId != request.ChapterId || current.SourceContext != metadata.SourceContext) return;
        token.ThrowIfCancellationRequested();
        if (!await _plans.SaveIfCachedAsync(plan, token).ConfigureAwait(false)) return;
        token.ThrowIfCancellationRequested();
        if (!profile.Equals(await GetProfileAsync(token).ConfigureAwait(false))) return;
        _invalidation.Publish(CacheInvalidation.ForChapters(request.BookId, [request.ChapterIndex], CacheInvalidationAspect.Coverage));
    }

    private async Task<TextProfileFingerprint> GetProfileAsync(CancellationToken token) =>
        TextProfileFingerprint.Create(_settings.Current.ToTextSegmentationOptions(),
            await _rules.GetAllAsync(token).ConfigureAwait(false));

    private void ReportFailure(Exception exception)
    {
        try { _failureReporter?.ReportCompletenessUnavailable(exception); }
        catch { /* Diagnostics cannot interrupt the worker. */ }
    }

    private sealed class Demand
    {
        public IReadOnlyList<SpeechPlanRepairRequest> Requests { get; set; } = [];
        public HashSet<SpeechPlanRepairRequest> Attempted { get; } = [];
        public CancellationTokenRegistration Registration { get; set; }
    }
}
