using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Builds the shared chapter speech plan used by playback and background cache consumers.
/// </summary>
public interface IChapterSpeechPlanService
{
    Task<ChapterSpeechPlanBuildResult> BuildAsync(
        string chapterId,
        string chapterText,
        TextSegmentationOptions options,
        CancellationToken cancellationToken);
}
