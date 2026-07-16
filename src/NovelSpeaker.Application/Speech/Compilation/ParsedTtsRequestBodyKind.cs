namespace NovelSpeaker.Application.Speech.Compilation;

/// <summary>
/// Describes how a compiled request body should be sent over HTTP.
/// </summary>
public enum ParsedTtsRequestBodyKind
{
    None = 0,
    Json = 1,
    FormUrlEncoded = 2,
    RawText = 3
}
