namespace NovelSpeaker.Application.Books;

/// <summary>
/// Loads lightweight library items for the book list page.
/// </summary>
public interface IBookCatalogService
{
    Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken);
}
