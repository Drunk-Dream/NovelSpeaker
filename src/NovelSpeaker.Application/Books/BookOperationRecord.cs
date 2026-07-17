namespace NovelSpeaker.Application.Books;

public sealed record BookOperationRecord(
    string OperationId,
    BookOperationKind Kind,
    BookOperationPhase Phase,
    string BookId,
    IReadOnlyList<BookOperationPath> Paths,
    DateTimeOffset CreatedAt);
