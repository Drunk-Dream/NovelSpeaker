namespace NovelSpeaker.Application.Books.Library;

/// <summary>
/// Coordinates one durable deletion while leaving database and file primitives behind a semantic port.
/// </summary>
public sealed class BookDeletionService : IBookDeletionService, IBookSourceRemovalService
{
    private readonly IBookDeletionOperationStore _operationStore;
    private readonly BookMutationGate _mutations;
    private readonly BookSourceChanges _sourceChanges;
    private readonly IEnumerable<IBookRemovalWorkStopper> _workStoppers;
    private readonly IEnumerable<IBookRemovalStorageLeaseProvider> _storageLeases;

    public BookDeletionService(IBookDeletionOperationStore operationStore, BookMutationGate mutations, BookSourceChanges sourceChanges,
        IEnumerable<IBookRemovalWorkStopper> workStoppers, IEnumerable<IBookRemovalStorageLeaseProvider> storageLeases)
    {
        _operationStore = operationStore;
        _mutations = mutations;
        _sourceChanges = sourceChanges;
        _workStoppers = workStoppers;
        _storageLeases = storageLeases;
    }

    public async Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // SQLite and recursive file staging may complete synchronously; keep them off the UI thread.
        return await _mutations.RunAsync(() => Task.Run(async () =>
        {
            var preparation = await PrepareAndExecuteAsync(request.BookId, null,
                () => _operationStore.BeginAsync(request, cancellationToken), cancellationToken).ConfigureAwait(false);
            return preparation?.Result;
        }, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task<BookSourceRemoveResult?> RemoveAsync(BookSourceRemoveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceId);
        return await _mutations.RunAsync(() => Task.Run(async () =>
        {
            var preparation = await PrepareAndExecuteAsync(request.BookId, request.SourceId,
                () => _operationStore.BeginSourceRemovalAsync(request, cancellationToken), cancellationToken).ConfigureAwait(false);
            return preparation is not null ? new BookSourceRemoveResult(request.BookId, request.SourceId, preparation.DeletesBook) : null;
        }, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private async Task<BookDeletionPreparation?> PrepareAndExecuteAsync(string bookId, string? sourceId,
        Func<Task<BookDeletionPreparation?>> begin, CancellationToken cancellationToken)
    {
        foreach (var owner in _workStoppers)
            await owner.StopForRemovalAsync(bookId, sourceId, cancellationToken).ConfigureAwait(false);
        var leases = new List<IDisposable>();
        try
        {
            foreach (var provider in _storageLeases)
                leases.Add(await provider.AcquireAsync(cancellationToken).ConfigureAwait(false));
            var preparation = await begin().ConfigureAwait(false);
            return await ExecuteAsync(preparation, sourceId, cancellationToken).ConfigureAwait(false) ? preparation : null;
        }
        finally
        {
            foreach (var lease in leases.AsEnumerable().Reverse()) lease.Dispose();
        }
    }

    private async Task<bool> ExecuteAsync(BookDeletionPreparation? preparation, string? removedSourceId, CancellationToken cancellationToken)
    {
        if (preparation is null)
        {
            return false;
        }

        try
        {
            await _operationStore.CommitAsync(preparation, cancellationToken).ConfigureAwait(false);
            if (removedSourceId is not null)
            {
                _sourceChanges.Publish(new BookCommittedChange.SourceRemoved(preparation.Result.BookId, removedSourceId));
            }
            if (preparation.DeletesBook)
            {
                _sourceChanges.Publish(new BookCommittedChange.BookRemoved(preparation.Result.BookId));
            }
            else if (preparation.ActiveSourceId is { } sourceId)
            {
                _sourceChanges.Publish(new BookCommittedChange.ActiveSourceChanged(preparation.Result.BookId, sourceId, null));
            }
            await _operationStore.CompleteAsync(preparation, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            // Rollback checks database ownership before restoring, so a commit/state-update interruption stays recoverable.
            await _operationStore.RollbackAsync(preparation, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
