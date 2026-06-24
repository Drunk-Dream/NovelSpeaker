using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents either a locally playable audio file or a classified playback generation failure.
/// </summary>
public sealed record PlaybackAudioResult(
    string? FilePath,
    bool IsUsingCache,
    TtsExecutionFailure? Failure)
{
    public bool IsSuccess => FilePath is not null && Failure is null;
}
