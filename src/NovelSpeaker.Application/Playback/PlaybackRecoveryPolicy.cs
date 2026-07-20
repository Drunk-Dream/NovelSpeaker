using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Immutable inputs for a single playback recovery decision.
/// </summary>
internal sealed record PlaybackRecoveryInput(
    TtsErrorKind FailureKind,
    string FailureMessage,
    int ConsecutiveSegmentFailureCount,
    bool HasNextSegment,
    bool IsCorruptAudio,
    bool CorruptAudioRecoveryAttempted);

/// <summary>
/// Immutable output for a single playback recovery decision.
/// </summary>
internal sealed record PlaybackRecoveryDecision(
    bool ShouldInvalidateAudio,
    bool ShouldRetryCurrentSegment,
    bool ShouldPause,
    int ConsecutiveSegmentFailureCount,
    string Message,
    bool CanRetry,
    bool CanSkip);

/// <summary>
/// Decides how the coordinator should recover from one current-segment failure.
/// It has no mutable state and never publishes UI events.
/// </summary>
internal sealed class PlaybackRecoveryPolicy
{
    internal const int FailurePauseThreshold = 2;

    public PlaybackRecoveryDecision Decide(PlaybackRecoveryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.FailureKind == TtsErrorKind.Cancelled)
        {
            return new PlaybackRecoveryDecision(
                ShouldInvalidateAudio: false,
                ShouldRetryCurrentSegment: false,
                ShouldPause: false,
                input.ConsecutiveSegmentFailureCount,
                input.FailureMessage,
                CanRetry: false,
                CanSkip: false);
        }

        if (input.IsCorruptAudio && !input.CorruptAudioRecoveryAttempted)
        {
            return new PlaybackRecoveryDecision(
                ShouldInvalidateAudio: true,
                ShouldRetryCurrentSegment: true,
                ShouldPause: false,
                input.ConsecutiveSegmentFailureCount,
                input.FailureMessage,
                CanRetry: true,
                input.HasNextSegment);
        }

        // A corrupt local file is already a recovery attempt boundary. Keep the
        // existing failure count semantics for the repeated-corruption path; the
        // generation failure counter is for consecutive TTS segment failures.
        var failureCount = input.IsCorruptAudio
            ? input.ConsecutiveSegmentFailureCount
            : checked(input.ConsecutiveSegmentFailureCount + 1);
        var shouldPause = !input.IsCorruptAudio && failureCount >= FailurePauseThreshold;
        var message = shouldPause
            ? $"已连续 {failureCount} 段播放失败，请重试、跳过或停止。"
            : input.FailureMessage;

        return new PlaybackRecoveryDecision(
            ShouldInvalidateAudio: false,
            ShouldRetryCurrentSegment: false,
            shouldPause,
            failureCount,
            message,
            CanRetry: true,
            input.HasNextSegment);
    }
}
