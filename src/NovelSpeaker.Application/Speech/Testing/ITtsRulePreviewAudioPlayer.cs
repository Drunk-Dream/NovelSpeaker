namespace NovelSpeaker.Application.Speech.Testing;

/// <summary>
/// Plays a rule-test audio file without exposing the general playback session to Speech.
/// </summary>
public interface ITtsRulePreviewAudioPlayer : IAsyncDisposable
{
    Task<TtsRulePreviewPlaybackResult> PlayAsync(
        string filePath,
        CancellationToken cancellationToken);
}

public sealed record TtsRulePreviewPlaybackResult(
    bool IsSuccess,
    string? FailureMessage,
    Exception? FailureException = null);
