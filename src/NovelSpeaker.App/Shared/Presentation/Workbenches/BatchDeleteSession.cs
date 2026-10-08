namespace NovelSpeaker.App.Shared.Presentation.Workbenches;

/// <summary>Owns one confirmed batch's busy lifetime and per-item continuation.</summary>
internal sealed class BatchDeleteSession
{
    private bool _active;

    public async Task<BatchDeleteResult?> RunAsync<T>(
        Func<CancellationToken, Task<IReadOnlyList<T>>> prepare,
        Func<T, CancellationToken, Task<bool>> delete,
        Func<CancellationToken, Task> refresh,
        Action<bool> setBusy,
        CancellationToken cancellationToken)
    {
        if (_active) return null;
        _active = true;
        var ownsBusy = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = await prepare(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (items.Count == 0) return null;
            setBusy(true);
            ownsBusy = true;
            var succeeded = 0;
            var skipped = 0;
            var failed = 0;
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await delete(item, cancellationToken)) succeeded++;
                    else skipped++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { failed++; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            await refresh(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new BatchDeleteResult(succeeded, skipped, failed);
        }
        finally
        {
            if (ownsBusy) setBusy(false);
            _active = false;
        }
    }
}

internal sealed record BatchDeleteResult(int Succeeded, int Skipped, int Failed);
