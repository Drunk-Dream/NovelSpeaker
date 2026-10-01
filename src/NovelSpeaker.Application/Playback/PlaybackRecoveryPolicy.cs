using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Immutable inputs for a single playback recovery decision.
/// </summary>
internal sealed record PlaybackRecoveryInput(
    TtsErrorKind FailureKind,
    string FailureMessage,
    int ConsecutiveSegmentFailureCount,
    bool IsCorruptAudio,
    bool CorruptAudioRecoveryAttempted);

/// <summary>
/// Immutable output for a single playback recovery decision.
/// </summary>
internal sealed record PlaybackRecoveryDecision(
    bool ShouldInvalidateAudio,
    bool ShouldRetryCurrentSegment,
    bool ShouldSkipCurrentSegment,
    bool ShouldPause,
    int ConsecutiveSegmentFailureCount,
    string Message,
    bool CanRetry);

/// <summary>
/// Decides how the coordinator should recover from one current-segment failure.
/// It has no mutable state and never publishes UI events.
/// </summary>
internal sealed class PlaybackRecoveryPolicy
{
    internal const int FailurePauseThreshold = 3;

    public PlaybackRecoveryDecision Decide(PlaybackRecoveryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.FailureKind == TtsErrorKind.Cancelled)
        {
            return new PlaybackRecoveryDecision(
                ShouldInvalidateAudio: false,
                ShouldRetryCurrentSegment: false,
                ShouldSkipCurrentSegment: false,
                ShouldPause: false,
                input.ConsecutiveSegmentFailureCount,
                input.FailureMessage,
                CanRetry: false);
        }

        if (input.IsCorruptAudio && !input.CorruptAudioRecoveryAttempted)
        {
            return new PlaybackRecoveryDecision(
                ShouldInvalidateAudio: true,
                ShouldRetryCurrentSegment: true,
                ShouldSkipCurrentSegment: false,
                ShouldPause: false,
                input.ConsecutiveSegmentFailureCount,
                input.FailureMessage,
                CanRetry: true);
        }

        var failureCount = checked(input.ConsecutiveSegmentFailureCount + 1);
        var shouldPause = failureCount >= FailurePauseThreshold;
        var message = shouldPause
            ? $"已连续跳过 {failureCount} 个播放失败的段落，已暂停。请重试、切换语音服务或停止。"
            : input.FailureMessage;

        return new PlaybackRecoveryDecision(
            ShouldInvalidateAudio: false,
            ShouldRetryCurrentSegment: false,
            ShouldSkipCurrentSegment: true,
            shouldPause,
            failureCount,
            message,
            CanRetry: true);
    }
}
