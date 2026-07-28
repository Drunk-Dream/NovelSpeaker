using NovelSpeaker.Application.Cache;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>Persisted identity metadata for one current body segment.</summary>
public sealed record ChapterSpeechPlanSegment(
    int OrderIndex,
    SpeechSegmentKind SegmentKind,
    int SourceStartOffset,
    int SourceLength,
    Fingerprint SpeechTextHash);
