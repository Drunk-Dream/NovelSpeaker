namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Contains a redacted preview of a converted request after template evaluation.
/// </summary>
public sealed record TtsRequestPreview(
    string Url,
    string? Header,
    string? RequestOptionsJson);
