namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Represents the trimmed runtime view of a persisted HTTP TTS rule.
/// </summary>
public sealed record NormalizedHttpTtsRule(
    long RuleId,
    string Name,
    string Url,
    string? DeclaredContentType,
    string? ConcurrentRate,
    string? Header,
    bool EnableSessionCookieJar,
    IReadOnlyList<string> UnsupportedFields);
