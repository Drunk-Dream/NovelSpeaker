using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Compilation;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Describes one runtime TTS generation request for a specific book segment.
/// </summary>
public sealed record PlaybackAudioRequest(
    string BookId,
    int ChapterIndex,
    int SegmentIndex,
    string SpeechText,
    long RuleId,
    HttpTtsRule SourceRule,
    NormalizedHttpTtsRule NormalizedRule,
    int SpeakSpeed,
    Guid SessionId);
