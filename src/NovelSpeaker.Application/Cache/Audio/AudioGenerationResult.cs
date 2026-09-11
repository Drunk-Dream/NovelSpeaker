using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Execution;

namespace NovelSpeaker.Application.Cache.Audio;

/// <summary>
/// Represents either a locally playable audio file or a classified playback generation failure.
/// </summary>
public sealed record AudioGenerationResult(
    string? FilePath,
    bool IsUsingCache,
    TtsExecutionFailure? Failure)
{
    public bool IsSuccess => FilePath is not null && Failure is null;
}
