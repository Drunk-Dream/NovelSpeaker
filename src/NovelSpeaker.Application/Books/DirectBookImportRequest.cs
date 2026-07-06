namespace NovelSpeaker.Application.Books;

/// <summary>
/// Describes one direct TXT import attempt, optionally with a user-selected encoding override.
/// </summary>
public sealed record DirectBookImportRequest(
    string FilePath,
    string? EncodingOverride);
