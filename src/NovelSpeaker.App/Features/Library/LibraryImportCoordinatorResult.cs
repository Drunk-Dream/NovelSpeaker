using NovelSpeaker.Application.Books;

namespace NovelSpeaker.App.Features.Library;

public sealed record LibraryImportCoordinatorResult(
    LibraryImportCoordinatorStatus Status,
    BookImportFailureReason? FailureReason = null);
