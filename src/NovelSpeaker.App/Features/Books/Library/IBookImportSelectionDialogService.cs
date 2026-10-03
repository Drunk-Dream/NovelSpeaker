using NovelSpeaker.Application.Books;

namespace NovelSpeaker.App.Features.Books.Library;

public interface IBookImportSelectionDialogService
{
    Task<BookImportSelection?> ShowAsync(IReadOnlyList<BookImportCandidate> candidates, CancellationToken cancellationToken);
}
