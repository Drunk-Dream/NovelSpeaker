using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>Applies enabled global rules to already segmented runtime text.</summary>
public interface IRegexReplacementPipeline
{
    Task<RegexReplacementPipelineResult> ApplyAsync(
        IReadOnlyList<SpeechSegment> sourceSegments,
        CancellationToken cancellationToken);
}
