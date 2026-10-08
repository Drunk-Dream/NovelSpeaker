namespace NovelSpeaker.Application.Books;

public sealed record BookDeletionPreparation(
    string OperationId,
    BookDeleteResult Result,
    bool DeletesBook = true,
    string? ActiveSourceId = null);
