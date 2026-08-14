using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Shared.Presentation.Rules;

public sealed class RuleDocumentInteraction(
    IPresentationFileDialogService fileDialogs,
    IPresentationClipboard clipboard,
    IUserDocumentFileOperations fileOperations) : IRuleDocumentInteraction
{
    private static readonly PresentationFileDialogOptions OpenOptions =
        new("JSON files (*.json)|*.json|All files (*.*)|*.*");

    public async Task<RuleImportDocument?> PickImportAsync(CancellationToken cancellationToken)
    {
        var path = await fileDialogs.PickOpenFileAsync(OpenOptions, cancellationToken);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var metadata = await fileOperations.GetMetadataAsync(path, cancellationToken);
        var json = await fileOperations.ReadTextAsync(path, cancellationToken);
        return new RuleImportDocument(json, metadata?.FileName ?? "所选规则文件");
    }

    public async Task<RuleImportDocument?> ReadClipboardAsync(CancellationToken cancellationToken)
    {
        var json = await clipboard.GetTextAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(json) ? null : new RuleImportDocument(json, "剪贴板");
    }

    public async Task<bool> ExportAsync(
        string suggestedFileName,
        string json,
        CancellationToken cancellationToken)
    {
        var path = await fileDialogs.PickSaveFileAsync(
            new PresentationFileDialogOptions(OpenOptions.Filter, suggestedFileName),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        await fileOperations.WriteTextAsync(path, json, cancellationToken);
        return true;
    }

    public Task CopyAsync(string json, CancellationToken cancellationToken) =>
        clipboard.SetTextAsync(json, cancellationToken);
}
