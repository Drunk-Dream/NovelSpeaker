namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Reports expected cache completeness-estimation fallbacks without exposing book text or storage paths.
/// </summary>
public interface ICacheWorkspaceFailureReporter
{
    void ReportEstimationFallback(Exception exception);
}
