namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Summarizes the result of generating a redacted request preview for one rule test.
/// </summary>
public sealed record TtsRuleTestPreviewResult(
    bool IsSuccess,
    string Message,
    TtsRequestPreview? Preview,
    IReadOnlyList<string> Warnings,
    TtsErrorKind? ErrorKind);
