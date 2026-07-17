namespace NovelSpeaker.Application.Books;

public sealed record BookDeletionPreparation(
    string OperationId,
    BookDeleteResult Result);
