namespace NovelSpeaker.App.Features.Books.Shared;

public interface IBookDeleteDialogService
{
    Task<BookDeleteDialogResult> ShowAsync(BookDeleteDialogRequest request, CancellationToken cancellationToken);
}
