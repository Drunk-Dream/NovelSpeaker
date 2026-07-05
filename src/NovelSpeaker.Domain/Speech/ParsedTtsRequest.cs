namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Represents one fully compiled HTTP TTS request that is ready to execute.
/// </summary>
public sealed record ParsedTtsRequest(
    long RuleId,
    string Method,
    Uri Url,
    IReadOnlyDictionary<string, string> Headers,
    ParsedTtsRequestBody Body,
    string? DeclaredContentType);
