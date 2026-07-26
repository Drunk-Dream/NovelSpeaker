using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Speech;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackRecoveryPolicyTests
{
    [Fact]
    public void First_corrupt_audio_failure_requests_one_invalidation_and_retry()
    {
        var policy = new PlaybackRecoveryPolicy();

        var decision = policy.Decide(new PlaybackRecoveryInput(
            TtsErrorKind.AudioDecode,
            "音频损坏。",
            ConsecutiveSegmentFailureCount: 0,
            IsCorruptAudio: true,
            CorruptAudioRecoveryAttempted: false));

        Assert.True(decision.ShouldInvalidateAudio);
        Assert.True(decision.ShouldRetryCurrentSegment);
        Assert.False(decision.ShouldPause);
        Assert.Equal(0, decision.ConsecutiveSegmentFailureCount);
    }

    [Fact]
    public void Repeated_corrupt_audio_failure_does_not_retry_again()
    {
        var policy = new PlaybackRecoveryPolicy();

        var decision = policy.Decide(new PlaybackRecoveryInput(
            TtsErrorKind.AudioDecode,
            "音频再次损坏。",
            ConsecutiveSegmentFailureCount: 0,
            IsCorruptAudio: true,
            CorruptAudioRecoveryAttempted: true));

        Assert.False(decision.ShouldInvalidateAudio);
        Assert.False(decision.ShouldRetryCurrentSegment);
        Assert.False(decision.ShouldPause);
        Assert.True(decision.CanRetry);
        Assert.Equal("音频再次损坏。", decision.Message);
    }

    [Theory]
    [InlineData(TtsErrorKind.Unauthorized)]
    [InlineData(TtsErrorKind.RateLimited)]
    [InlineData(TtsErrorKind.ServerError)]
    public void Http_classified_failures_are_not_retried_by_playback_policy(TtsErrorKind failureKind)
    {
        var policy = new PlaybackRecoveryPolicy();

        var firstFailure = policy.Decide(new PlaybackRecoveryInput(
            failureKind,
            "服务暂时不可用。",
            ConsecutiveSegmentFailureCount: 0,
            IsCorruptAudio: false,
            CorruptAudioRecoveryAttempted: false));

        var thresholdFailure = policy.Decide(new PlaybackRecoveryInput(
            failureKind,
            "服务暂时不可用。",
            firstFailure.ConsecutiveSegmentFailureCount,
            IsCorruptAudio: false,
            CorruptAudioRecoveryAttempted: false));

        Assert.False(firstFailure.ShouldRetryCurrentSegment);
        Assert.False(firstFailure.ShouldPause);
        Assert.Equal(1, firstFailure.ConsecutiveSegmentFailureCount);
        Assert.True(thresholdFailure.ShouldPause);
        Assert.Equal(2, thresholdFailure.ConsecutiveSegmentFailureCount);
        Assert.Contains("连续 2 段", thresholdFailure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancellation_is_not_projected_as_a_retryable_failure()
    {
        var policy = new PlaybackRecoveryPolicy();

        var decision = policy.Decide(new PlaybackRecoveryInput(
            TtsErrorKind.Cancelled,
            "已取消当前音频生成。",
            ConsecutiveSegmentFailureCount: 1,
            IsCorruptAudio: false,
            CorruptAudioRecoveryAttempted: false));

        Assert.False(decision.ShouldInvalidateAudio);
        Assert.False(decision.ShouldRetryCurrentSegment);
        Assert.False(decision.ShouldPause);
        Assert.False(decision.CanRetry);
        Assert.Equal(1, decision.ConsecutiveSegmentFailureCount);
    }
}
