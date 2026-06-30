namespace NovelSpeaker.App.Dialogs;

public interface IBookDeleteDialogService
{
    Task<BookDeleteDialogResult> ShowAsync(BookDeleteDialogRequest request, CancellationToken cancellationToken);
}
