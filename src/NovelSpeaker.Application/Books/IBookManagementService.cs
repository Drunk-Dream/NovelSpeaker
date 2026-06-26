namespace NovelSpeaker.Application.Books;

/// <summary>
/// Owns book details, metadata updates, cache cleanup, and atomic deletion.
/// </summary>
public interface IBookManagementService
{
    Task<BookDetails?> GetBookDetailsAsync(string bookId, CancellationToken cancellationToken);

    Task<BookDetails> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken);

    Task<long> ClearBookCacheAsync(string bookId, CancellationToken cancellationToken);

    Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken);
}
