namespace NovelSpeaker.App.Shared.Presentation.Rules;

/// <summary>Owns file and clipboard interactions shared by rule management features.</summary>
public interface IRuleDocumentInteraction
{
    Task<RuleImportDocument?> PickImportAsync(CancellationToken cancellationToken);

    Task<RuleImportDocument?> ReadClipboardAsync(CancellationToken cancellationToken);

    Task<bool> ExportAsync(string suggestedFileName, string json, CancellationToken cancellationToken);

    Task CopyAsync(string json, CancellationToken cancellationToken);
}
