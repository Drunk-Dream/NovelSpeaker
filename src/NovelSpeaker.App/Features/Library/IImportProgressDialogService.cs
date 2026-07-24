using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Features.Library;

namespace NovelSpeaker.App.Features.Library;

public interface IImportProgressDialogService
{
    Task<LibraryImportCoordinatorResult> RunAsync(
        string fileName,
        Func<IProgress<BookImportProgress>, CancellationToken, Task<LibraryImportCoordinatorResult>> operation,
        CancellationToken cancellationToken);
}
