namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Owns process-level chapter speech-plan refreshes and drains them during shutdown.
/// </summary>
public interface ICacheWorkspaceBackgroundTaskOwner
{
    Task StopBackgroundOperationsAsync(CancellationToken cancellationToken);
}
