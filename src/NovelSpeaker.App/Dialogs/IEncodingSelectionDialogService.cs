using NovelSpeaker.Application.Books;

namespace NovelSpeaker.App.Dialogs;

public interface IEncodingSelectionDialogService
{
    Task<string?> ShowAsync(EncodingSelectionPrompt prompt, CancellationToken cancellationToken);
}
