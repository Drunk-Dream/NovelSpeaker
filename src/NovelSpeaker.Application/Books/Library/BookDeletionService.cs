namespace NovelSpeaker.Application.Books.Library;

/// <summary>
/// Coordinates one durable deletion while leaving database and file primitives behind a semantic port.
/// </summary>
public sealed class BookDeletionService : IBookDeletionService
{
    private readonly IBookDeletionOperationStore _operationStore;

    public BookDeletionService(IBookDeletionOperationStore operationStore)
    {
        _operationStore = operationStore;
    }

    public async Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var preparation = await _operationStore.BeginAsync(request, cancellationToken).ConfigureAwait(false);
        if (preparation is null)
        {
            return null;
        }

        try
        {
            await _operationStore.CommitAsync(preparation, cancellationToken).ConfigureAwait(false);
            await _operationStore.CompleteAsync(preparation, cancellationToken).ConfigureAwait(false);
            return preparation.Result;
        }
        catch
        {
            // Rollback checks database ownership before restoring, so a commit/state-update interruption stays recoverable.
            await _operationStore.RollbackAsync(preparation, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
