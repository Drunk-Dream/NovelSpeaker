using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Playback;

internal sealed record PlaybackSessionIdentity(Guid SessionId, string BookId, ActiveSourceContext? SourceContext);

/// <summary>Accepted device facts, never a second low-level player snapshot.</summary>
internal sealed record PlaybackAudioFacts(
    bool HasLoadedAudio,
    long PositionMilliseconds,
    long DurationMilliseconds,
    bool IsUsingCache)
{
    public static PlaybackAudioFacts Empty { get; } = new(false, 0, 0, false);
}

/// <summary>
/// One immutable read of the authoritative runtime. Only PlaybackRuntime replaces it;
/// UI snapshots and effect executors cannot write it back.
/// </summary>
internal sealed record PlaybackRuntimeState(
    PlaybackSessionIdentity? Identity,
    PlaybackBookContent? Book,
    PlaybackPosition? Position,
    ResolvedSpeechProvider? Provider,
    int SpeakSpeed,
    PlaybackState State,
    long ResumePositionMilliseconds,
    int ConsecutiveSegmentFailureCount,
    PlaybackAudioFacts Audio,
    string? Message,
    bool CanRetry,
    long ContentRevision,
    TtsErrorKind? LastFailureKind = null,
    string? RecoveredCorruptSegmentKey = null)
{
    public static PlaybackRuntimeState Idle { get; } = new(
        null, null, null, null, AppSettings.DefaultSpeakSpeedValue, PlaybackState.Idle,
        0, 0, PlaybackAudioFacts.Empty, "请选择一本书并开始播放。", false, 0);

    public long PositionForSave => Audio.HasLoadedAudio
        ? Audio.PositionMilliseconds
        : ResumePositionMilliseconds;
}

/// <summary>Already resolved input; loading and synthesis happen outside the runtime.</summary>
internal sealed record PlaybackSessionTarget(
    PlaybackBookContent Book,
    PlaybackPosition? Position,
    ResolvedSpeechProvider? Provider,
    int SpeakSpeed,
    PlaybackState State,
    long ResumePositionMilliseconds = 0,
    int ConsecutiveSegmentFailureCount = 0,
    string? Message = null,
    bool CanRetry = false);
