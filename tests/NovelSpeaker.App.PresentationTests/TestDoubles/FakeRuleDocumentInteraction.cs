using NovelSpeaker.App.Shared.Presentation.Rules;

namespace NovelSpeaker.App.PresentationTests.TestDoubles;

internal sealed class FakeRuleDocumentInteraction : IRuleDocumentInteraction
{
    public RuleImportDocument? FileDocument { get; set; }

    public RuleImportDocument? ClipboardDocument { get; set; }

    public TaskCompletionSource<RuleImportDocument?>? FileDocumentGate { get; set; }

    public int FileReadCount { get; private set; }

    public int ClipboardReadCount { get; private set; }

    public string? ExportedFileName { get; private set; }

    public string? ExportedJson { get; private set; }

    public string? CopiedJson { get; private set; }

    public bool ExportAccepted { get; set; } = true;

    public Task<RuleImportDocument?> PickImportAsync(CancellationToken cancellationToken)
    {
        FileReadCount++;
        return FileDocumentGate is null
            ? Task.FromResult(FileDocument)
            : FileDocumentGate.Task.WaitAsync(cancellationToken);
    }

    public Task<RuleImportDocument?> ReadClipboardAsync(CancellationToken cancellationToken)
    {
        ClipboardReadCount++;
        return Task.FromResult(ClipboardDocument);
    }

    public Task<bool> ExportAsync(string suggestedFileName, string json, CancellationToken cancellationToken)
    {
        ExportedFileName = suggestedFileName;
        ExportedJson = json;
        return Task.FromResult(ExportAccepted);
    }

    public Task CopyAsync(string json, CancellationToken cancellationToken)
    {
        CopiedJson = json;
        return Task.CompletedTask;
    }
}
