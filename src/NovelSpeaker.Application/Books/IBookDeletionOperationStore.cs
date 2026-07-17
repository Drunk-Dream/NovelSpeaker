namespace NovelSpeaker.Application.Books;

/// <summary>
/// Performs the durable file staging and atomic database portion of a book deletion.
/// </summary>
public interface IBookDeletionOperationStore
{
    Task<BookDeletionPreparation?> BeginAsync(BookDeleteRequest request, CancellationToken cancellationToken);

    Task CommitAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken);

    Task CompleteAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken);

    Task RollbackAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken);
}
