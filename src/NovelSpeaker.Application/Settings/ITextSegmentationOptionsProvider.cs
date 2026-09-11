using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Settings;

/// <summary>
/// Provides the current global text-segmentation options without exposing storage details.
/// </summary>
public interface ITextSegmentationOptionsProvider
{
    TextSegmentationOptions GetCurrent();
}
