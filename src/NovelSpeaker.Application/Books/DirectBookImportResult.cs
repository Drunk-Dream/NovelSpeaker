namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents the outcome of a direct TXT import attempt.
/// </summary>
public sealed record DirectBookImportResult(
    DirectBookImportStatus Status,
    BookImportResult? ImportedBook = null,
    EncodingSelectionPrompt? EncodingSelectionPrompt = null,
    BookImportFailureReason? FailureReason = null);
