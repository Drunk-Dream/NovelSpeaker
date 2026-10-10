using NovelSpeaker.Application.Books;

namespace NovelSpeaker.App.Features.Books.Library;

public interface IBookImportMetadataDialogService
{
    Task<BookImportIdentity?> ShowAsync(BookImportIdentity defaults, CancellationToken cancellationToken);
}
