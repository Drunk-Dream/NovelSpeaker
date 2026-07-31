namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Reports expected chapter-content failures without exposing source text or storage details.
/// </summary>
public interface IBookPlaybackContentFailureReporter
{
    void ReportChapterReadFailure(Exception exception);
}
