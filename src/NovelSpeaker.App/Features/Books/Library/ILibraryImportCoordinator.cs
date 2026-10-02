namespace NovelSpeaker.App.Features.Books.Library;

public interface ILibraryImportCoordinator
{
    Task<LibraryImportCoordinatorResult> ImportAsync(
        string filePath,
        CancellationToken cancellationToken);
}
