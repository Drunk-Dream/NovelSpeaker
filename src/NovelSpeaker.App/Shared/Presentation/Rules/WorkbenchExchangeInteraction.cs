namespace NovelSpeaker.App.Shared.Presentation.Rules;

/// <summary>Produces one exchange document, then writes it to the chosen interaction surface.</summary>
internal sealed class WorkbenchExchangeInteraction(IRuleDocumentInteraction documents)
{
    public async Task<ExchangeWriteResult> WriteAsync(
        Func<CancellationToken, Task<string?>> buildDocument,
        bool clipboard,
        string suggestedFileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await buildDocument(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (json is null) return ExchangeWriteResult.MissingDocument;
        if (clipboard) await documents.CopyAsync(json, cancellationToken);
        else if (!await documents.ExportAsync(suggestedFileName, json, cancellationToken))
            return ExchangeWriteResult.Cancelled;
        cancellationToken.ThrowIfCancellationRequested();
        return ExchangeWriteResult.Completed;
    }
}

internal enum ExchangeWriteResult { MissingDocument, Cancelled, Completed }
