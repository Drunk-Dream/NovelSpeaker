using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>Runtime projection plus safe per-rule execution errors.</summary>
public sealed record RegexReplacementPipelineResult(
    IReadOnlyList<SpeechSegment> Segments,
    IReadOnlyDictionary<Guid, string> RuleErrors,
    IReadOnlyList<RegexReplacementRule>? AppliedRules = null);
