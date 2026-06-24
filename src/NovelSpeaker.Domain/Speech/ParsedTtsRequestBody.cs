namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Holds the normalized HTTP body generated from one TTS rule.
/// </summary>
public sealed record ParsedTtsRequestBody(
    ParsedTtsRequestBodyKind Kind,
    string? RawText,
    IReadOnlyDictionary<string, string>? FormFields)
{
    public static ParsedTtsRequestBody None { get; } =
        new(ParsedTtsRequestBodyKind.None, null, null);
}
