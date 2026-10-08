using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Playback;

internal sealed record PlaybackSessionIdentity(Guid SessionId, string BookId, ActiveSourceContext? SourceContext);

/// <summary>Identity of the user's logical position within one playback session.</summary>
internal sealed record PlaybackTargetIdentity(
    Guid SessionId,
    string BookId,
    ActiveSourceContext? SourceContext,
    long Revision);

internal sealed record PlaybackLogicalTarget(PlaybackTargetIdentity Identity, PlaybackPosition Position);

internal enum PlaybackIntent
{
    Play,
    Pause,
    Stop
}

internal enum PlaybackPreparationKind
{
    Initial,
    Recovery
}

/// <summary>
/// Identifies one preparation by the session, committed target and exact synthesis
/// inputs. Cache reuse may produce the same synthesis identity for another target.
/// </summary>
internal sealed record PlaybackAudioPreparationIdentity(
    PlaybackSessionIdentity Session,
    PlaybackTargetIdentity Target,
    AudioCacheIdentity Synthesis,
    Guid AttemptId);

internal sealed record PlaybackAudioPreparation(
    PlaybackAudioPreparationIdentity Identity,
    PlaybackPreparationKind Kind);

/// <summary>Accepted device facts, never a second low-level player snapshot.</summary>
internal sealed record PlaybackAudioFacts(
    bool HasLoadedAudio,
    long PositionMilliseconds,
    long DurationMilliseconds,
    bool IsUsingCache,
    PlaybackTargetIdentity? TargetIdentity = null,
    PlaybackAudioPreparationIdentity? PreparationIdentity = null)
{
    public static PlaybackAudioFacts Empty { get; } = new(false, 0, 0, false, null, null);
}

/// <summary>
/// One immutable read of the authoritative runtime. Only PlaybackRuntime replaces it;
/// UI snapshots and effect executors cannot write it back.
/// </summary>
internal sealed record PlaybackRuntimeState(
    PlaybackSessionIdentity? Identity,
    PlaybackBookContent? Book,
    PlaybackLogicalTarget? Target,
    ResolvedSpeechProvider? Provider,
    int SpeakSpeed,
    PlaybackIntent Intent,
    PlaybackAudioPreparation? Preparation,
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
        null, null, null, null, AppSettings.DefaultSpeakSpeedValue, PlaybackIntent.Stop, null, PlaybackState.Idle,
        0, 0, PlaybackAudioFacts.Empty, "请选择一本书并开始播放。", false, 0);

    public PlaybackPosition? Position => Target?.Position;

    public long TargetRevision => Target?.Identity.Revision ?? 0;

    public long PositionForSave => Audio.HasLoadedAudio && Audio.TargetIdentity == Target?.Identity
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
