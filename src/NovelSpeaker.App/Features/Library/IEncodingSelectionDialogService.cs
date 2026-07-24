using NovelSpeaker.Application.Books;

namespace NovelSpeaker.App.Features.Library;

public interface IEncodingSelectionDialogService
{
    Task<string?> ShowAsync(EncodingSelectionPrompt prompt, CancellationToken cancellationToken);
}
