using System.Collections.Concurrent;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Deduplicates chapter plan repairs and drains them as one process-owned background lifetime.
/// </summary>
public sealed class SpeechPlanRepairCoordinator : ISpeechPlanRepairCoordinator
{
    private const int MaximumConcurrency = 2;
    private readonly IBookPlaybackContentService? _contentService;
    private readonly IAppSettingsService _settingsService;
    private readonly IRegexReplacementRuleRepository? _regexRuleRepository;
    private readonly IChapterSpeechPlanStore? _speechPlanStore;
    private readonly ICacheInvalidationCoordinator? _invalidationCoordinator;
    private readonly ICacheWorkspaceFailureReporter? _failureReporter;
    private readonly ConcurrentDictionary<RepairKey, Lazy<Task>> _repairs = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _concurrency = new(MaximumConcurrency, MaximumConcurrency);
    private readonly object _gate = new();
    private Task? _stopTask;
    private bool _stopping;
    private bool _disposed;

    public SpeechPlanRepairCoordinator(
        IBookPlaybackContentService? contentService,
        IAppSettingsService settingsService,
        IRegexReplacementRuleRepository? regexRuleRepository = null,
        IChapterSpeechPlanStore? speechPlanStore = null,
        ICacheInvalidationCoordinator? invalidationCoordinator = null,
        ICacheWorkspaceFailureReporter? failureReporter = null)
    {
        _contentService = contentService;
        _settingsService = settingsService;
        _regexRuleRepository = regexRuleRepository;
        _speechPlanStore = speechPlanStore;
        _invalidationCoordinator = invalidationCoordinator;
        _failureReporter = failureReporter;
    }

    public Task RequestAsync(
        SpeechPlanRepairRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        Task repairTask;
        lock (_gate)
        {
            if (_stopping || _disposed)
            {
                return Task.CompletedTask;
            }

            var repair = _repairs.GetOrAdd(
                new RepairKey(request.BookId, request.ChapterIndex),
                _ => new Lazy<Task>(
                    () => RunRepairAsync(request),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            repairTask = repair.Value;
        }

        return repairTask.WaitAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task stopTask;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _stopping = true;
            _lifetimeCancellation.Cancel();
            _stopTask ??= Task.WhenAll(
                _repairs.Values
                    .Where(static repair => repair.IsValueCreated)
                    .Select(static repair => repair.Value)
                    .ToArray());
            ObserveFaults(_stopTask);
            stopTask = _stopTask;
        }

        await stopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _disposed = true;
            }

            _concurrency.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }

    private async Task RunRepairAsync(SpeechPlanRepairRequest request)
    {
        var entered = false;
        try
        {
            if (_contentService is null)
            {
                return;
            }

            await _concurrency
                .WaitAsync(_lifetimeCancellation.Token)
                .ConfigureAwait(false);
            entered = true;

            while (true)
            {
                var profileBefore = await GetCurrentTextProfileAsync(_lifetimeCancellation.Token)
                    .ConfigureAwait(false);
                var chapter = await _contentService
                    .GetChapterAsync(request.BookId, request.ChapterIndex, _lifetimeCancellation.Token)
                    .ConfigureAwait(false);
                if (chapter is null)
                {
                    return;
                }

                var profileAfter = await GetCurrentTextProfileAsync(_lifetimeCancellation.Token)
                    .ConfigureAwait(false);
                if (!profileBefore.Equals(profileAfter) ||
                    !await IsPersistedPlanCurrentAsync(request, profileAfter).ConfigureAwait(false))
                {
                    continue;
                }

                _invalidationCoordinator?.Publish(
                    CacheInvalidation.ForChapters(
                        request.BookId,
                        [request.ChapterIndex],
                        CacheInvalidationAspect.Coverage));
                return;
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
        finally
        {
            if (entered)
            {
                try
                {
                    _concurrency.Release();
                }
                catch (ObjectDisposedException) when (_disposed)
                {
                }
            }

            _repairs.TryRemove(new RepairKey(request.BookId, request.ChapterIndex), out _);
        }
    }

    private async Task<TextProfileFingerprint> GetCurrentTextProfileAsync(
        CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current;
        IReadOnlyList<RegexReplacementRule> rules = _regexRuleRepository is null
            ? Array.Empty<RegexReplacementRule>()
            : await _regexRuleRepository
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(false);
        return TextProfileFingerprint.Create(settings.ToTextSegmentationOptions(), rules);
    }

    private async Task<bool> IsPersistedPlanCurrentAsync(
        SpeechPlanRepairRequest request,
        TextProfileFingerprint currentProfile)
    {
        ChapterSpeechPlan? plan = null;
        if (_speechPlanStore is not null)
        {
            plan = await _speechPlanStore
                .GetAsync(request.ChapterId, _lifetimeCancellation.Token)
                .ConfigureAwait(false);
        }

        var stableProfile = await GetCurrentTextProfileAsync(_lifetimeCancellation.Token)
            .ConfigureAwait(false);
        if (!currentProfile.Equals(stableProfile))
        {
            return false;
        }

        return _speechPlanStore is null ||
            (plan is not null &&
            plan.State == ChapterSpeechPlanState.Ready &&
            plan.TextProfileFingerprint.Equals(stableProfile));
    }

    private void ReportFailure(Exception exception)
    {
        try
        {
            _failureReporter?.ReportCompletenessUnavailable(exception);
        }
        catch
        {
            // Failure reporting is best effort and must not strand the owner task.
        }
    }

    private static void ObserveFaults(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed record RepairKey(string BookId, int ChapterIndex);
}
