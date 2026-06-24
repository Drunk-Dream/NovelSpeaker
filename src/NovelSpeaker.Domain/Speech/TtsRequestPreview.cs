namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Contains a redacted preview of a converted request after template evaluation.
/// </summary>
public sealed record TtsRequestPreview(
    string Method,
    string Url,
    string? HeadersJson,
    string? BodyPreview,
    string? DeclaredContentType);
