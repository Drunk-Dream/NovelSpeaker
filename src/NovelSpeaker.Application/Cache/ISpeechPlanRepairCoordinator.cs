namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Owns one process worker for the current visible stale-plan demands.
/// </summary>
internal interface ISpeechPlanRepairCoordinator : ISpeechPlanRepairLifetime, IAsyncDisposable
{
    /// <summary>
    /// Replaces the lightweight demands for one visible window; cancellation withdraws it.
    /// </summary>
    void UpdateDemand(
        IReadOnlyList<SpeechPlanRepairRequest> requests,
        CancellationToken visibilityToken);
}
