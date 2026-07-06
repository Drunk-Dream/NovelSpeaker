using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Library;

namespace NovelSpeaker.App.Dialogs;

public interface IImportProgressDialogService
{
    Task<LibraryImportCoordinatorResult> RunAsync(
        string fileName,
        Func<IProgress<BookImportProgress>, CancellationToken, Task<LibraryImportCoordinatorResult>> operation,
        CancellationToken cancellationToken);
}
