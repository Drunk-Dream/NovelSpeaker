using NovelSpeaker.App.Shared.Presentation.Rules;

namespace NovelSpeaker.App.Shared.Presentation.Workbenches;

/// <summary>
/// Coordinates one page's import source and Busy ownership without changing editor drafts.
/// </summary>
internal sealed class WorkbenchImportSession
{
    private int _active;

    public async Task<WorkbenchImportExecution<T>?> RunAsync<T>(
        Func<CancellationToken, Task<RuleImportDocument?>> readDocument,
        Func<RuleImportDocument, CancellationToken, Task<T>> import,
        Func<bool> isBusy,
        Action<bool> setBusy,
        CancellationToken cancellationToken,
        Action? reportMissingDocument = null)
    {
        ArgumentNullException.ThrowIfNull(readDocument);
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(isBusy);
        ArgumentNullException.ThrowIfNull(setBusy);

        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            return null;
        }

        var ownsBusy = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = await readDocument(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (document is null)
            {
                reportMissingDocument?.Invoke();
                return null;
            }

            if (isBusy())
            {
                return null;
            }

            setBusy(true);
            ownsBusy = true;
            cancellationToken.ThrowIfCancellationRequested();
            var result = await import(document, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new WorkbenchImportExecution<T>(document, result);
        }
        finally
        {
            if (ownsBusy)
            {
                setBusy(false);
            }

            Volatile.Write(ref _active, 0);
        }
    }
}

internal sealed record WorkbenchImportExecution<T>(RuleImportDocument Document, T Result);
