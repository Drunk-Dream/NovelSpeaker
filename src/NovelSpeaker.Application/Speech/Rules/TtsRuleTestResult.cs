using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>
/// Summarizes the result of testing one HTTP TTS rule from the rules page.
/// </summary>
public sealed record TtsRuleTestResult(
    bool IsSuccess,
    string Message,
    TtsRequestPreview? Preview,
    IReadOnlyList<string> Warnings,
    TtsErrorKind? ErrorKind,
    int? StatusCode,
    string? ResponseContentType,
    string? ResponseSummary,
    TimeSpan? RetryAfter);
