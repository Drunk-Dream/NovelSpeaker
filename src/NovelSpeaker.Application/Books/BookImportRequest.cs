namespace NovelSpeaker.Application.Books;

/// <summary>
/// Describes one TXT import analysis request from the UI.
/// </summary>
public sealed record BookImportRequest(
    string FilePath,
    string? EncodingOverride);
