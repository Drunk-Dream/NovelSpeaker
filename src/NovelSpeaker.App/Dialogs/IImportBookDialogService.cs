namespace NovelSpeaker.App.Dialogs;

public interface IImportBookDialogService
{
    Task<ImportBookDialogOutcome> ShowAsync(string filePath, CancellationToken cancellationToken);
}
