using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Cache;

/// <summary>Provides the runtime projection and its computed, unpersisted identity plan.</summary>
public sealed record ChapterSpeechPlanBuildResult(
    IReadOnlyList<SpeechSegment> Segments,
    ChapterSpeechPlan Plan);
