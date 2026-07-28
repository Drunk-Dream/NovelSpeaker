using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>Provides both the current runtime projection and its persisted identity plan.</summary>
public sealed record ChapterSpeechPlanBuildResult(
    IReadOnlyList<SpeechSegment> Segments,
    ChapterSpeechPlan Plan);
