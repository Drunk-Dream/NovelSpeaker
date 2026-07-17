namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reads detached book summaries, details, and chapter projections for library experiences.
/// </summary>
public interface IBookLibraryQuery
{
    Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken);

    Task<BookDetailsHeader?> GetBookDetailsHeaderAsync(string bookId, CancellationToken cancellationToken);

    Task<BookDetails?> GetBookDetailsAsync(string bookId, CancellationToken cancellationToken);
}
