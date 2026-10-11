namespace NovelSpeaker.Application.Books;

/// <summary>
/// Describes one TXT import attempt. ConfirmedIdentity resumes an interrupted import
/// with the user's final Title/Author; encoding is resolved before identity confirmation.
/// </summary>
public sealed record DirectBookImportRequest(
    string FilePath,
    string? EncodingOverride,
    string SourceFileName,
    BookImportIdentity? ConfirmedIdentity = null);
