using NovelSpeaker.Application.Books;
namespace NovelSpeaker.App.Features.Books.Library;

public interface IImportProgressDialogService
{
    Task<LibraryImportCoordinatorResult> RunAsync(
        string fileName,
        Func<IProgress<BookImportProgress>, CancellationToken, Task<LibraryImportCoordinatorResult>> operation,
        CancellationToken cancellationToken);
}
