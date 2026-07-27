namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Reports expected cache completeness fallbacks without exposing book text or storage paths.
/// </summary>
public interface ICacheWorkspaceFailureReporter
{
    void ReportCompletenessUnavailable(Exception exception);
}
