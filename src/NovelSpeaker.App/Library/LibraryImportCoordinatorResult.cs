using NovelSpeaker.Application.Books;

namespace NovelSpeaker.App.Library;

public sealed record LibraryImportCoordinatorResult(
    LibraryImportCoordinatorStatus Status,
    BookImportFailureReason? FailureReason = null);
