namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Captures a classified HTTP TTS failure and any safe diagnostic details for the UI.
/// </summary>
public sealed record TtsExecutionFailure(
    TtsErrorKind Kind,
    string Message,
    int? StatusCode,
    string? ResponseSummary,
    string? ResponseContentType,
    TimeSpan? RetryAfter);
