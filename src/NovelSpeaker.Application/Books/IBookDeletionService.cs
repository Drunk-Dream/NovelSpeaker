namespace NovelSpeaker.Application.Books;

/// <summary>
/// Deletes a book and its owned persisted resources as one semantic operation.
/// </summary>
public interface IBookDeletionService
{
    Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken);
}
