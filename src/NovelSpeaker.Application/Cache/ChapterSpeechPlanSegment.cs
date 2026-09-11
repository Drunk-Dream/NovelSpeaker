using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Cache;

/// <summary>Persisted identity metadata for one current body segment.</summary>
public sealed record ChapterSpeechPlanSegment(
    int OrderIndex,
    SpeechSegmentKind SegmentKind,
    int SourceStartOffset,
    int SourceLength,
    Fingerprint SpeechTextHash);
