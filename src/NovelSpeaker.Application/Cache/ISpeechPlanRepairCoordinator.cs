namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Owns process-scoped repair of missing or stale chapter speech plans.
/// </summary>
internal interface ISpeechPlanRepairCoordinator : ISpeechPlanRepairLifetime, IAsyncDisposable
{
    /// <summary>
    /// Registers a repair. Cancelling the returned wait does not cancel the shared repair.
    /// </summary>
    Task RequestAsync(
        SpeechPlanRepairRequest request,
        CancellationToken cancellationToken);
}
