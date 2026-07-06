namespace NovelSpeaker.Application.Books;

/// <summary>
/// Carries the information needed to ask the user for a manual text encoding choice.
/// </summary>
public sealed record EncodingSelectionPrompt(
    string FilePath,
    string FileName,
    string Message,
    string DefaultEncoding,
    IReadOnlyList<string> AvailableEncodings);
