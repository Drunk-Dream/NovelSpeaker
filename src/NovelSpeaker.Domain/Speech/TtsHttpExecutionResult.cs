namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Represents either a validated audio result or a classified execution failure.
/// </summary>
public sealed record TtsHttpExecutionResult(
    TtsAudioResponse? Audio,
    TtsExecutionFailure? Failure)
{
    public bool IsSuccess => Audio is not null && Failure is null;
}
