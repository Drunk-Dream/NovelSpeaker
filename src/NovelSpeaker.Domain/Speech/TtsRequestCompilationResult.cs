namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Represents either a compiled request plus preview or a classified compilation failure.
/// </summary>
public sealed record TtsRequestCompilationResult(
    ParsedTtsRequest? Request,
    TtsRequestPreview? Preview,
    IReadOnlyList<string> Warnings,
    TtsExecutionFailure? Failure)
{
    public bool IsSuccess => Request is not null && Preview is not null && Failure is null;
}
