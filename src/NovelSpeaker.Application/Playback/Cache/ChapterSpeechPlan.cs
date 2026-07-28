using NovelSpeaker.Application.Cache;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>One chapter's current, replaceable speech plan.</summary>
public sealed record ChapterSpeechPlan(
    string ChapterId,
    Fingerprint ChapterRevisionHash,
    TextProfileFingerprint TextProfileFingerprint,
    Fingerprint PlanOutputHash,
    ChapterSpeechPlanState State,
    int BodySegmentCount,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ChapterSpeechPlanSegment> Segments);
