namespace NovelSpeaker.App.Features.BookDetails;

public interface IBookDeleteDialogService
{
    Task<BookDeleteDialogResult> ShowAsync(BookDeleteDialogRequest request, CancellationToken cancellationToken);
}
