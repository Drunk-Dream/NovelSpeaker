namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Reports expected cache-completeness fallbacks without exposing book text or storage paths.
/// </summary>
public interface ICacheCompletenessFailureReporter
{
    void ReportCompletenessUnavailable(Exception exception);
}
