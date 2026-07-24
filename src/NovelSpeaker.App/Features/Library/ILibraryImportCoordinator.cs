using NovelSpeaker.Application.Books;

namespace NovelSpeaker.App.Features.Library;

public interface ILibraryImportCoordinator
{
    Task<LibraryImportCoordinatorResult> ImportAsync(
        string filePath,
        IProgress<BookImportProgress>? inlineProgress,
        CancellationToken cancellationToken);
}
