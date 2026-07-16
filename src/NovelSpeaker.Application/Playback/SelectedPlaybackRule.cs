using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Compilation;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the rule currently selected for runtime playback.
/// </summary>
public sealed record SelectedPlaybackRule(
    long RuleId,
    string RuleName,
    HttpTtsRule SourceRule,
    NormalizedHttpTtsRule NormalizedRule);
