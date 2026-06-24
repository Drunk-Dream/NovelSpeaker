namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Represents the runtime view of a converted HTTP TTS rule.
/// </summary>
public sealed record NormalizedHttpTtsRule(
    long RuleId,
    string Name,
    NormalizedTemplate UrlTemplate,
    NormalizedTemplate? HeaderTemplate,
    NormalizedTemplate? RequestOptionsTemplate,
    string? DeclaredContentType,
    string? ConcurrentRate,
    bool EnableSessionCookieJar,
    IReadOnlyList<string> UnsupportedFields);
